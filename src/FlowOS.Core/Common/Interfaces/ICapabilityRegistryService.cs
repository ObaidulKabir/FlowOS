using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Core.Common.Interfaces;

public record CapabilityBindingDto(
    Guid Id,
    Guid TenantId,
    string CapabilityName,
    string Transport,
    string EndpointUrl,
    string? AuthRef,
    string? RequestSchemaVersion,
    string? ResponseSchemaVersion,
    string RetryPolicy,
    int TimeoutMs,
    bool IsEnabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public interface ICapabilityRegistryService
{
    Task<CapabilityBindingDto> UpsertAsync(
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
        CancellationToken ct = default);

    Task<IReadOnlyList<CapabilityBindingDto>> ListAsync(
        Guid tenantId,
        string? capabilityName = null,
        bool? enabledOnly = null,
        CancellationToken ct = default);

    Task<(bool IsValid, string Message, CapabilityBindingDto? Binding)> ValidateBindingAsync(
        Guid tenantId,
        string capabilityName,
        CancellationToken ct = default);

    Task<CapabilityBindingDto?> ResolveAsync(
        Guid tenantId,
        string capabilityName,
        CancellationToken ct = default);
}
