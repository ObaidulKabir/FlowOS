using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;

namespace FlowOS.Core.Common.Services;

/// <summary>
/// Null-object implementation of <see cref=""IPluginBindingRegistryService""/> used as default fallback.
/// </summary>
public sealed class NullPluginBindingRegistryService : IPluginBindingRegistryService
{
    public static readonly NullPluginBindingRegistryService Instance = new();

    public Task<PluginBindingDto> UpsertAsync(
        Guid tenantId,
        string bindingType,
        string sourceName,
        string providerName,
        bool isEnabled = true,
        string? configurationJson = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Plugin bindings cannot be updated on the null registry.");

    public Task<IReadOnlyList<PluginBindingDto>> ListAsync(
        Guid tenantId,
        string? bindingType = null,
        string? sourceName = null,
        bool? enabledOnly = null,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PluginBindingDto>>(Array.Empty<PluginBindingDto>());

    public Task<string?> ResolveProviderNameAsync(
        Guid tenantId,
        string bindingType,
        string sourceName,
        CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task<Dictionary<string, string>> ResolveBindingsAsync(
        Guid tenantId,
        string bindingType,
        CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public Task<PluginBindingDto?> GetEnabledAsync(
        Guid tenantId,
        string bindingType,
        string sourceName,
        CancellationToken ct = default) =>
        Task.FromResult<PluginBindingDto?>(null);

    public Task<AgentProviderConfiguration?> GetAgentSecretsAsync(
        Guid tenantId,
        string sourceName,
        CancellationToken ct = default) =>
        Task.FromResult<AgentProviderConfiguration?>(null);
}
