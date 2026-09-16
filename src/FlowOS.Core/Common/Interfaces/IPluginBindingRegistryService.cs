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
}

public static class AgentProviderKinds
{
    public const string OpenAi = "openai";
    public const string Anthropic = "anthropic";
    public const string AzureOpenAi = "azure-openai";
    public const string Google = "google";
    public const string Custom = "custom";
    public const string FlowosRisk = "flowos-risk";

    public static bool IsKnown(string? providerName) =>
        !string.IsNullOrWhiteSpace(providerName) &&
        providerName.Trim().ToLowerInvariant() is
            OpenAi or Anthropic or AzureOpenAi or Google or Custom or FlowosRisk;
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
    object? Configuration = null);

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
}
