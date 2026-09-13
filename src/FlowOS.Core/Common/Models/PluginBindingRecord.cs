using System;

namespace FlowOS.Core.Common.Models;

public class PluginBindingRecord
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string BindingType { get; private set; } = string.Empty; // action | decision
    public string SourceName { get; private set; } = string.Empty; // blueprint name
    public string ProviderName { get; private set; } = string.Empty; // server plugin provider
    public bool IsEnabled { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    private PluginBindingRecord()
    {
    }

    public PluginBindingRecord(
        Guid tenantId,
        string bindingType,
        string sourceName,
        string providerName,
        bool isEnabled = true)
    {
        TenantId = tenantId;
        BindingType = Normalize(bindingType);
        SourceName = Normalize(sourceName);
        ProviderName = providerName?.Trim() ?? string.Empty;
        IsEnabled = isEnabled;
    }

    public void Update(string providerName, bool isEnabled)
    {
        ProviderName = providerName?.Trim() ?? ProviderName;
        IsEnabled = isEnabled;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;
}
