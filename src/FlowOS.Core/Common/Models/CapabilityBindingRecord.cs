using System;

namespace FlowOS.Core.Common.Models;

public class CapabilityBindingRecord
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string CapabilityName { get; private set; } = string.Empty;
    public string Transport { get; private set; } = "http";
    public string EndpointUrl { get; private set; } = string.Empty;
    public string? AuthRef { get; private set; }
    public string? RequestSchemaVersion { get; private set; }
    public string? ResponseSchemaVersion { get; private set; }
    public string RetryPolicy { get; private set; } = "default";
    public int TimeoutMs { get; private set; } = 10000;
    public bool IsEnabled { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    private CapabilityBindingRecord()
    {
    }

    public CapabilityBindingRecord(
        Guid tenantId,
        string capabilityName,
        string endpointUrl,
        string transport = "http",
        int timeoutMs = 10000,
        string? authRef = null,
        string? requestSchemaVersion = null,
        string? responseSchemaVersion = null,
        string? retryPolicy = null,
        bool isEnabled = true)
    {
        TenantId = tenantId;
        CapabilityName = capabilityName?.Trim() ?? string.Empty;
        EndpointUrl = endpointUrl?.Trim() ?? string.Empty;
        Transport = string.IsNullOrWhiteSpace(transport) ? "http" : transport.Trim();
        TimeoutMs = timeoutMs > 0 ? timeoutMs : 10000;
        AuthRef = authRef?.Trim();
        RequestSchemaVersion = requestSchemaVersion?.Trim();
        ResponseSchemaVersion = responseSchemaVersion?.Trim();
        RetryPolicy = string.IsNullOrWhiteSpace(retryPolicy) ? "default" : retryPolicy.Trim();
        IsEnabled = isEnabled;
    }

    public void Update(
        string endpointUrl,
        string transport,
        int timeoutMs,
        string? authRef,
        string? requestSchemaVersion,
        string? responseSchemaVersion,
        string? retryPolicy,
        bool isEnabled)
    {
        EndpointUrl = endpointUrl?.Trim() ?? EndpointUrl;
        Transport = string.IsNullOrWhiteSpace(transport) ? Transport : transport.Trim();
        TimeoutMs = timeoutMs > 0 ? timeoutMs : TimeoutMs;
        AuthRef = authRef?.Trim();
        RequestSchemaVersion = requestSchemaVersion?.Trim();
        ResponseSchemaVersion = responseSchemaVersion?.Trim();
        RetryPolicy = string.IsNullOrWhiteSpace(retryPolicy) ? RetryPolicy : retryPolicy.Trim();
        IsEnabled = isEnabled;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
