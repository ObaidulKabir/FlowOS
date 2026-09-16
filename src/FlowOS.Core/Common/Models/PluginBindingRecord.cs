using System;

namespace FlowOS.Core.Common.Models;

public class PluginBindingRecord
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string BindingType { get; private set; } = string.Empty; // action | decision | agent | prompt
    public string SourceName { get; private set; } = string.Empty; // blueprint name
    public string ProviderName { get; private set; } = string.Empty; // server plugin provider
    public bool IsEnabled { get; private set; } = true;
    public string? ConfigurationJson { get; private set; }
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
        ConfigurationJson = null;
    }

    public void Update(string providerName, bool isEnabled, string? configurationJson = null)
    {
        ProviderName = providerName?.Trim() ?? ProviderName;
        IsEnabled = isEnabled;
        if (configurationJson != null)
            ConfigurationJson = configurationJson;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;
}
