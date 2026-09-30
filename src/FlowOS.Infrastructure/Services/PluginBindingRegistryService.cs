using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public class PluginBindingRegistryService : IPluginBindingRegistryService
{
    private readonly FlowOSDbContext _dbContext;

    public PluginBindingRegistryService(FlowOSDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PluginBindingDto> UpsertAsync(
        Guid tenantId,
        string bindingType,
        string sourceName,
        string providerName,
        bool isEnabled = true,
        string? configurationJson = null,
        CancellationToken ct = default)
    {
        var normalizedType = NormalizeBindingType(bindingType);
        var normalizedSource = NormalizeKey(sourceName);
        var normalizedProvider = providerName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedType))
            throw new ArgumentException("bindingType is required.");
        if (normalizedType != PluginBindingTypes.Action &&
            normalizedType != PluginBindingTypes.Decision &&
            normalizedType != PluginBindingTypes.Agent &&
            normalizedType != PluginBindingTypes.Prompt)
            throw new ArgumentException($"Unsupported bindingType '{bindingType}'. Use '{PluginBindingTypes.Action}', '{PluginBindingTypes.Decision}', '{PluginBindingTypes.Agent}', or '{PluginBindingTypes.Prompt}'.");
        if (string.IsNullOrWhiteSpace(normalizedSource))
            throw new ArgumentException("sourceName is required.");
        if (string.IsNullOrWhiteSpace(normalizedProvider))
            throw new ArgumentException("providerName is required.");

        var existing = await _dbContext.PluginBindings
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.BindingType == normalizedType &&
                x.SourceName == normalizedSource, ct);

        string? mergedConfig = null;
        if (existing == null)
        {
            existing = new PluginBindingRecord(
                tenantId,
                normalizedType,
                normalizedSource,
                normalizedProvider,
                isEnabled);
            _dbContext.PluginBindings.Add(existing);
            if (configurationJson != null)
            {
                mergedConfig = MergeConfiguration(normalizedType, null, configurationJson);
                existing.Update(normalizedProvider, isEnabled, mergedConfig);
            }
        }
        else
        {
            mergedConfig = MergeConfiguration(normalizedType, existing.ConfigurationJson, configurationJson);
            existing.Update(normalizedProvider, isEnabled, mergedConfig);
        }

        if (normalizedType == PluginBindingTypes.Agent && mergedConfig != null)
        {
            var parsed = AgentProviderConfiguration.Parse(mergedConfig);
            if (parsed?.IsDefault == true)
            {
                var otherProviders = await _dbContext.PluginBindings
                    .Where(x =>
                        x.TenantId == tenantId &&
                        x.BindingType == PluginBindingTypes.Agent &&
                        x.SourceName != normalizedSource)
                    .ToListAsync(ct);

                foreach (var other in otherProviders)
                {
                    var otherConfig = AgentProviderConfiguration.Parse(other.ConfigurationJson);
                    if (otherConfig?.IsDefault == true)
                    {
                        otherConfig.IsDefault = false;
                        other.Update(other.ProviderName, other.IsEnabled, System.Text.Json.JsonSerializer.Serialize(otherConfig));
                    }
                }
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        return ToDto(existing);
    }

    public async Task<IReadOnlyList<PluginBindingDto>> ListAsync(
        Guid tenantId,
        string? bindingType = null,
        string? sourceName = null,
        bool? enabledOnly = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.PluginBindings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        var normalizedType = NormalizeBindingType(bindingType);
        if (!string.IsNullOrWhiteSpace(normalizedType))
        {
            query = query.Where(x => x.BindingType == normalizedType);
        }

        var normalizedSource = NormalizeKey(sourceName);
        if (!string.IsNullOrWhiteSpace(normalizedSource))
        {
            query = query.Where(x => x.SourceName == normalizedSource);
        }

        if (enabledOnly == true)
        {
            query = query.Where(x => x.IsEnabled);
        }

        var items = await query
            .OrderBy(x => x.BindingType)
            .ThenBy(x => x.SourceName)
            .ToListAsync(ct);

        return items.Select(ToDto).ToList();
    }

    public async Task<string?> ResolveProviderNameAsync(
        Guid tenantId,
        string bindingType,
        string sourceName,
        CancellationToken ct = default)
    {
        var normalizedType = NormalizeBindingType(bindingType);
        var normalizedSource = NormalizeKey(sourceName);
        if (string.IsNullOrWhiteSpace(normalizedType) || string.IsNullOrWhiteSpace(normalizedSource))
            return null;

        var record = await _dbContext.PluginBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.BindingType == normalizedType &&
                x.SourceName == normalizedSource &&
                x.IsEnabled, ct);

        return record?.ProviderName;
    }

    public async Task<Dictionary<string, string>> ResolveBindingsAsync(
        Guid tenantId,
        string bindingType,
        CancellationToken ct = default)
    {
        var normalizedType = NormalizeBindingType(bindingType);
        if (string.IsNullOrWhiteSpace(normalizedType))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var records = await _dbContext.PluginBindings
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.BindingType == normalizedType &&
                x.IsEnabled)
            .ToListAsync(ct);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in records)
        {
            if (!string.IsNullOrWhiteSpace(item.SourceName) && !string.IsNullOrWhiteSpace(item.ProviderName))
            {
                map[item.SourceName] = item.ProviderName;
            }
        }

        return map;
    }

    public async Task<PluginBindingDto?> GetEnabledAsync(
        Guid tenantId,
        string bindingType,
        string sourceName,
        CancellationToken ct = default)
    {
        var normalizedType = NormalizeBindingType(bindingType);
        var normalizedSource = NormalizeKey(sourceName);
        if (string.IsNullOrWhiteSpace(normalizedType) || string.IsNullOrWhiteSpace(normalizedSource))
            return null;

        var record = await _dbContext.PluginBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.BindingType == normalizedType &&
                x.SourceName == normalizedSource &&
                x.IsEnabled, ct);

        return record == null ? null : ToDto(record);
    }

    public async Task<AgentProviderConfiguration?> GetAgentSecretsAsync(
        Guid tenantId,
        string sourceName,
        CancellationToken ct = default)
    {
        var normalizedSource = NormalizeKey(sourceName);
        if (string.IsNullOrWhiteSpace(normalizedSource))
            return null;

        var record = await _dbContext.PluginBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.BindingType == PluginBindingTypes.Agent &&
                x.SourceName == normalizedSource &&
                x.IsEnabled, ct);

        if (record == null && normalizedSource == "default")
        {
            var all = await _dbContext.PluginBindings
                .AsNoTracking()
                .Where(x =>
                    x.TenantId == tenantId &&
                    x.BindingType == PluginBindingTypes.Agent &&
                    x.IsEnabled)
                .ToListAsync(ct);
            record = all.FirstOrDefault(r =>
                AgentProviderConfiguration.Parse(r.ConfigurationJson)?.IsDefault == true);
        }

        return record == null ? null : AgentProviderConfiguration.Parse(record.ConfigurationJson);
    }

    public async Task<PluginBindingDto?> GetDefaultAgentProviderAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        var records = await _dbContext.PluginBindings
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.BindingType == PluginBindingTypes.Agent &&
                x.IsEnabled)
            .ToListAsync(ct);

        var defaultRecord = records.FirstOrDefault(r =>
            AgentProviderConfiguration.Parse(r.ConfigurationJson)?.IsDefault == true);

        return defaultRecord == null ? null : ToDto(defaultRecord);
    }

    private static PluginBindingDto ToDto(PluginBindingRecord x) =>
        new(
            x.Id,
            x.TenantId,
            x.BindingType,
            x.SourceName,
            x.ProviderName,
            x.IsEnabled,
            x.CreatedAtUtc,
            x.UpdatedAtUtc,
            x.BindingType == PluginBindingTypes.Agent
                ? AgentProviderConfiguration.Redact(x.ConfigurationJson)
                : x.BindingType == PluginBindingTypes.Prompt
                    ? AgentPromptConfiguration.Public(x.ConfigurationJson)
                    : null,
            x.FlowOsVersion);

    private static string? MergeConfiguration(string bindingType, string? existing, string? incoming)
    {
        if (incoming == null) return existing;
        if (bindingType == PluginBindingTypes.Agent)
            return AgentProviderConfiguration.Merge(existing, incoming);
        if (bindingType == PluginBindingTypes.Prompt)
            return AgentPromptConfiguration.Merge(existing, incoming);
        return incoming;
    }

    private static string NormalizeBindingType(string? bindingType) =>
        bindingType?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string NormalizeKey(string? key) =>
        key?.Trim().ToLowerInvariant() ?? string.Empty;
}
