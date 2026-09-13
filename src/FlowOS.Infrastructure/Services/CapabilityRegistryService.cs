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

public class CapabilityRegistryService : ICapabilityRegistryService
{
    private readonly FlowOSDbContext _dbContext;

    public CapabilityRegistryService(FlowOSDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CapabilityBindingDto> UpsertAsync(
        Guid tenantId,
        string capabilityName,
        string endpointUrl,
        string transport = "http",
        int timeoutMs = 10000,
        string? authRef = null,
        string? requestSchemaVersion = null,
        string? responseSchemaVersion = null,
        string? retryPolicy = null,
        bool isEnabled = true,
        CancellationToken ct = default)
    {
        var normalizedName = NormalizeCapabilityName(capabilityName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("capabilityName is required.");
        }

        var existing = await _dbContext.CapabilityBindings
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.CapabilityName == normalizedName, ct);

        if (existing == null)
        {
            existing = new CapabilityBindingRecord(
                tenantId,
                normalizedName,
                endpointUrl,
                transport,
                timeoutMs,
                authRef,
                requestSchemaVersion,
                responseSchemaVersion,
                retryPolicy,
                isEnabled);
            _dbContext.CapabilityBindings.Add(existing);
        }
        else
        {
            existing.Update(
                endpointUrl,
                transport,
                timeoutMs,
                authRef,
                requestSchemaVersion,
                responseSchemaVersion,
                retryPolicy,
                isEnabled);
        }

        await _dbContext.SaveChangesAsync(ct);
        return ToDto(existing);
    }

    public async Task<IReadOnlyList<CapabilityBindingDto>> ListAsync(
        Guid tenantId,
        string? capabilityName = null,
        bool? enabledOnly = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.CapabilityBindings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(capabilityName))
        {
            var normalizedName = NormalizeCapabilityName(capabilityName);
            query = query.Where(x => x.CapabilityName == normalizedName);
        }

        if (enabledOnly == true)
        {
            query = query.Where(x => x.IsEnabled);
        }

        var items = await query
            .OrderBy(x => x.CapabilityName)
            .ToListAsync(ct);

        return items.Select(ToDto).ToList();
    }

    public async Task<(bool IsValid, string Message, CapabilityBindingDto? Binding)> ValidateBindingAsync(
        Guid tenantId,
        string capabilityName,
        CancellationToken ct = default)
    {
        var binding = await ResolveAsync(tenantId, capabilityName, ct);
        if (binding == null)
        {
            return (false, "Capability binding not found.", null);
        }

        if (!binding.IsEnabled)
        {
            return (false, "Capability binding is disabled.", binding);
        }

        if (!string.Equals(binding.Transport, "http", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Transport '{binding.Transport}' is not supported yet. Use 'http'.", binding);
        }

        if (!Uri.TryCreate(binding.EndpointUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (false, "Capability endpoint must be an absolute HTTP/HTTPS URL.", binding);
        }

        return (true, "Capability binding is valid.", binding);
    }

    public async Task<CapabilityBindingDto?> ResolveAsync(
        Guid tenantId,
        string capabilityName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(capabilityName)) return null;
        var normalizedName = NormalizeCapabilityName(capabilityName);

        var record = await _dbContext.CapabilityBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.CapabilityName == normalizedName, ct);

        return record == null ? null : ToDto(record);
    }

    private static CapabilityBindingDto ToDto(CapabilityBindingRecord x) =>
        new(
            x.Id,
            x.TenantId,
            x.CapabilityName,
            x.Transport,
            x.EndpointUrl,
            x.AuthRef,
            x.RequestSchemaVersion,
            x.ResponseSchemaVersion,
            x.RetryPolicy,
            x.TimeoutMs,
            x.IsEnabled,
            x.CreatedAtUtc,
            x.UpdatedAtUtc);

    private static string NormalizeCapabilityName(string? capabilityName) =>
        capabilityName?.Trim().ToLowerInvariant() ?? string.Empty;
}
