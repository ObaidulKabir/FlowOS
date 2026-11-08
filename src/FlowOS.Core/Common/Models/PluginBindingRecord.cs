using System;
using FlowOS.Domain;

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

    /// <summary>FlowOS release this AI Context binding was last authored against.</summary>
    public string FlowOsVersion { get; private set; } = FlowOsRelease.Version;

    /// <summary>
    /// The tenant application this binding belongs to, or null for a tenant-shared binding. Only prompt
    /// and profile (agent) bindings can belong to an application; providers, tools and decision
    /// bindings are always tenant-shared. A step in an application's workflow resolves an alias in that
    /// application first, then among the tenant-shared bindings.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

    private PluginBindingRecord()
    {
    }

    public PluginBindingRecord(
        Guid tenantId,
        string bindingType,
        string sourceName,
        string providerName,
        bool isEnabled = true,
        Guid? applicationId = null)
    {
        TenantId = tenantId;
        BindingType = Normalize(bindingType);
        SourceName = Normalize(sourceName);
        ProviderName = providerName?.Trim() ?? string.Empty;
        IsEnabled = isEnabled;
        ConfigurationJson = null;
        FlowOsVersion = FlowOsRelease.Version;
        ApplicationId = applicationId == Guid.Empty ? null : applicationId;
    }

    /// <summary>Re-points the binding at an application (tenant backup restore maps application ids).</summary>
    public void AssignToApplication(Guid? applicationId)
    {
        ApplicationId = applicationId == Guid.Empty ? null : applicationId;
    }

    /// <summary>Binding types that may belong to an application. Everything else is tenant-shared.</summary>
    public static bool CanBelongToApplication(string? bindingType)
    {
        var normalized = Normalize(bindingType);
        return normalized == "prompt" || normalized == "profile";
    }

    public void Update(string providerName, bool isEnabled, string? configurationJson = null)
    {
        ProviderName = providerName?.Trim() ?? ProviderName;
        IsEnabled = isEnabled;
        if (configurationJson != null)
            ConfigurationJson = configurationJson;
        FlowOsVersion = FlowOsRelease.Version;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Replaces the stored configuration JSON without touching ProviderName, IsEnabled, FlowOsVersion,
    /// or UpdatedAtUtc. Used to backfill computed metadata (e.g. an API key fingerprint added in a
    /// later release) that is not a tenant-initiated configuration edit and must not look like one.
    /// </summary>
    public void BackfillConfigurationJson(string configurationJson)
    {
        ConfigurationJson = configurationJson;
    }

    private static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;
}
