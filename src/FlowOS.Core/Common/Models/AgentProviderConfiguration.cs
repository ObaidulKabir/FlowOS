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

        return JsonSerializer.Serialize(existing, JsonOptions);
    }

    public static AgentProviderPublicSettings Redact(string? json)
    {
        var parsed = Parse(json) ?? new AgentProviderConfiguration();
        return new AgentProviderPublicSettings(
            parsed.Model,
            parsed.Endpoint,
            !string.IsNullOrWhiteSpace(parsed.ApiKey));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record AgentProviderPublicSettings(string? Model, string? Endpoint, bool HasApiKey);
