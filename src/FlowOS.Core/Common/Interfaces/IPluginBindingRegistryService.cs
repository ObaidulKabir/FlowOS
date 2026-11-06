using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Models;

namespace FlowOS.Core.Common.Interfaces;

public static class PluginBindingTypes
{
    public const string Action = "action";
    public const string Decision = "decision";
    public const string Agent = "agent";
    public const string Prompt = "prompt";
    public const string Profile = "profile";
}

public static class AgentProviderKinds
{
    /// <summary>
    /// Tenant integration finding (post-H3/M2/P2 review): writing, deleting, or backfilling an agent
    /// provider binding - which can point a tenant's BYO LLM key at any endpoint - had no capability
    /// check at all. Required on upsert_agent_provider, delete_agent_provider,
    /// backfill_agent_provider_key_metadata, register_plugin_binding's agent path, and REST
    /// PUT /api/plugin-bindings for bindingType "agent". Granted to the reserved Admin role by default
    /// (see TenantSecurityDefaults); a narrower role or API-key scope can be granted it explicitly via
    /// grant_role_capability.
    /// </summary>
    public const string ManageCapability = "agent.manage";

    public const string OpenAi = "openai";
    public const string Anthropic = "anthropic";
    public const string AzureOpenAi = "azure-openai";
    public const string Google = "google";
    public const string Custom = "custom";
    public const string FlowosRisk = "flowos-risk";
    public const string FlowosHosted = "flowos-hosted";

    public static bool IsKnown(string? providerName) =>
        !string.IsNullOrWhiteSpace(providerName) &&
        providerName.Trim().ToLowerInvariant() is
            OpenAi or Anthropic or AzureOpenAi or Google or Custom or FlowosRisk or FlowosHosted;

    public static bool IsFlowOsHosted(string? providerName) =>
        string.Equals(providerName?.Trim(), FlowosHosted, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The alias "flowos-hosted" is platform-reserved: at runtime, a step whose <c>agentProvider</c>
    /// resolves to that exact alias always gets the platform's hosted lease, regardless of what
    /// providerName/apiKey/endpoint was saved under it. This check is used to reject writes that would
    /// save a non-hosted (BYO) providerName under that alias, rather than let the write silently succeed
    /// while the runtime ignores it.
    /// </summary>
    public static bool IsReservedAlias(string? alias) => IsFlowOsHosted(alias);

    public static bool IsByoLlm(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return false;
        var normalized = providerName.Trim().ToLowerInvariant();
        return normalized is OpenAi or Anthropic or AzureOpenAi or Google or Custom;
    }
}

public static class AgentPromptKinds
{
    public const string Markdown = "markdown";
    public const string FlowosPrompt = "flowos-prompt";

    public static bool IsKnown(string? providerName) =>
        !string.IsNullOrWhiteSpace(providerName) &&
        providerName.Trim().ToLowerInvariant() is Markdown or FlowosPrompt;
}

public record PluginBindingDto(
    Guid Id,
    Guid TenantId,
    string BindingType,
    string SourceName,
    string ProviderName,
    bool IsEnabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    object? Configuration = null,
    string? FlowOsVersion = null);

public interface IPluginBindingRegistryService
{
    Task<PluginBindingDto> UpsertAsync(
        Guid tenantId,
        string bindingType,
        string sourceName,
        string providerName,
        bool isEnabled = true,
        string? configurationJson = null,
        CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes a plugin binding (e.g. an agent provider) by tenant/bindingType/sourceName.
    /// Returns false when no matching binding exists; the caller maps that to a not-found error.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid tenantId,
        string bindingType,
        string sourceName,
        CancellationToken ct = default);

    /// <summary>
    /// Tenant integration finding M2 follow-up: computes and stores ApiKeyFingerprint/ApiKeyUpdatedAtUtc
    /// for this tenant's existing agent provider bindings whose key was stored before those fields
    /// existed (so they otherwise stay null forever unless the key happens to be rewritten). Decrypts
    /// the already-stored key only to compute a one-way fingerprint from it - the plaintext is never
    /// returned or logged. ApiKeyUpdatedAtUtc is backfilled from the binding's own UpdatedAtUtc as the
    /// best known approximation of when the key was last touched. A binding that already has a
    /// fingerprint, has no key at all, or whose stored key can't be decrypted, is left untouched.
    /// Returns the number of bindings updated.
    /// </summary>
    Task<int> BackfillAgentProviderKeyMetadataAsync(
        Guid tenantId,
        CancellationToken ct = default);

    Task<IReadOnlyList<PluginBindingDto>> ListAsync(
        Guid tenantId,
        string? bindingType = null,
        string? sourceName = null,
        bool? enabledOnly = null,
        CancellationToken ct = default);

    Task<string?> ResolveProviderNameAsync(
        Guid tenantId,
        string bindingType,
        string sourceName,
        CancellationToken ct = default);

    Task<Dictionary<string, string>> ResolveBindingsAsync(
        Guid tenantId,
        string bindingType,
        CancellationToken ct = default);

    Task<PluginBindingDto?> GetEnabledAsync(
        Guid tenantId,
        string bindingType,
        string sourceName,
        CancellationToken ct = default);

    /// <summary>
    /// Loads the tenant agent binding including the API key. Hosted agent runtime only — never MCP/REST list DTOs.
    /// </summary>
    Task<AgentProviderConfiguration?> GetAgentSecretsAsync(
        Guid tenantId,
        string sourceName,
        CancellationToken ct = default);

    /// <summary>
    /// Finds the tenant's designated default agent provider binding, if any.
    /// </summary>
    Task<PluginBindingDto?> GetDefaultAgentProviderAsync(
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Finds a composite agent profile by alias or role.
    /// </summary>
    Task<PluginBindingDto?> GetAgentProfileAsync(
        Guid tenantId,
        string aliasOrRole,
        CancellationToken ct = default);

    /// <summary>
    /// Finds a structured agent tool binding by business alias or name.
    /// </summary>
    Task<PluginBindingDto?> GetAgentToolAsync(
        Guid tenantId,
        string sourceName,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all structured agent tool bindings for a tenant.
    /// </summary>
    Task<IReadOnlyList<PluginBindingDto>> ListAgentToolsAsync(
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all composite agent profiles for a tenant.
    /// </summary>
    Task<IReadOnlyList<PluginBindingDto>> ListAgentProfilesAsync(
        Guid tenantId,
        CancellationToken ct = default);
}
