using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowOS.Core.Common.Models;

/// <summary>
/// Tenant-owned language-model settings stored on an <c>agent</c> plugin binding.
/// The API key never appears in Agent Context or list responses.
/// </summary>
public sealed class AgentProviderConfiguration
{
    public string? Model { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public bool IsDefault { get; set; }

    /// <summary>
    /// Maximum output tokens to request from the provider. When null, adapters use their own default.
    /// Particularly important for Anthropic which requires an explicit max_tokens value.
    /// </summary>
    public int? MaxTokens { get; set; }
    
    /// <summary>
    /// If execution fails due to rate-limiting or provider unavailability, FlowOS will fallback to this provider alias.
    /// </summary>
    public string? FallbackProviderAlias { get; set; }

    public static AgentProviderConfiguration? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<AgentProviderConfiguration>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string Merge(string? existingJson, string? incomingJson)
    {
        var existing = Parse(existingJson) ?? new AgentProviderConfiguration();
        var incoming = Parse(incomingJson);
        if (incoming == null) return existingJson ?? "{}";

        existing.Model = string.IsNullOrWhiteSpace(incoming.Model) ? existing.Model : incoming.Model.Trim();
        existing.Endpoint = string.IsNullOrWhiteSpace(incoming.Endpoint) ? existing.Endpoint : incoming.Endpoint.Trim();
        if (!string.IsNullOrWhiteSpace(incoming.ApiKey))
            existing.ApiKey = incoming.ApiKey.Trim();
        existing.IsDefault = incoming.IsDefault;
        existing.FallbackProviderAlias = string.IsNullOrWhiteSpace(incoming.FallbackProviderAlias) ? existing.FallbackProviderAlias : incoming.FallbackProviderAlias.Trim();

        return JsonSerializer.Serialize(existing, JsonOptions);
    }

    public static AgentProviderPublicSettings Redact(string? json)
    {
        var parsed = Parse(json) ?? new AgentProviderConfiguration();
        return new AgentProviderPublicSettings(
            parsed.Model,
            parsed.Endpoint,
            !string.IsNullOrWhiteSpace(parsed.ApiKey),
            parsed.IsDefault,
            parsed.FallbackProviderAlias);
    }

    public static string Serialize(AgentProviderConfiguration config) =>
        JsonSerializer.Serialize(config, JsonOptions);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record AgentProviderPublicSettings(string? Model, string? Endpoint, bool HasApiKey, bool IsDefault = false, string? FallbackProviderAlias = null);
