using System;
using System.Security.Cryptography;
using System.Text;
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
    /// Tenant integration finding M2: a non-reversible SHA-256-derived fingerprint of the currently
    /// stored API key, so a tenant can verify which key is active (e.g. after a rotation) without
    /// the key itself ever being exposed. Recomputed only when the key value actually changes.
    /// </summary>
    public string? ApiKeyFingerprint { get; set; }

    /// <summary>
    /// When the stored API key was last changed to a new value (not bumped merely by an unrelated
    /// field update). Null until a key has been set at least once.
    /// </summary>
    public DateTime? ApiKeyUpdatedAtUtc { get; set; }

    /// <summary>
    /// Maximum output tokens to request from the provider. When null, adapters use their own default.
    /// Particularly important for Anthropic which requires an explicit max_tokens value.
    /// </summary>
    public int? MaxTokens { get; set; }
    
    /// <summary>
    /// If execution fails due to rate-limiting or provider unavailability, FlowOS will fallback to this provider alias.
    /// </summary>
    public string? FallbackProviderAlias { get; set; }

    public bool EnablePromptAudit { get; set; }

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

    /// <param name="nowUtc">
    /// Current UTC time, supplied by the caller (via TimeProvider) rather than read here, used only
    /// to stamp <see cref="ApiKeyUpdatedAtUtc"/> when the incoming apiKey actually changes the stored
    /// value. Omit to merge without stamping (e.g. callers that only read/inspect, never persist).
    /// </param>
    public static string Merge(string? existingJson, string? incomingJson, DateTime? nowUtc = null)
    {
        var existing = Parse(existingJson) ?? new AgentProviderConfiguration();
        var incoming = Parse(incomingJson);
        if (incoming == null) return existingJson ?? "{}";

        existing.Model = string.IsNullOrWhiteSpace(incoming.Model) ? existing.Model : incoming.Model.Trim();
        existing.Endpoint = string.IsNullOrWhiteSpace(incoming.Endpoint) ? existing.Endpoint : incoming.Endpoint.Trim();
        if (!string.IsNullOrWhiteSpace(incoming.ApiKey))
        {
            var trimmedKey = incoming.ApiKey.Trim();
            if (trimmedKey != existing.ApiKey)
            {
                existing.ApiKeyFingerprint = ComputeFingerprint(trimmedKey);
                existing.ApiKeyUpdatedAtUtc = nowUtc ?? existing.ApiKeyUpdatedAtUtc;
            }
            existing.ApiKey = trimmedKey;
        }
        existing.IsDefault = incoming.IsDefault;
        existing.MaxTokens = incoming.MaxTokens ?? existing.MaxTokens;
        existing.FallbackProviderAlias = string.IsNullOrWhiteSpace(incoming.FallbackProviderAlias) ? existing.FallbackProviderAlias : incoming.FallbackProviderAlias.Trim();
        existing.EnablePromptAudit = incoming.EnablePromptAudit;

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
            parsed.MaxTokens,
            parsed.FallbackProviderAlias,
            parsed.EnablePromptAudit,
            parsed.ApiKeyFingerprint,
            parsed.ApiKeyUpdatedAtUtc);
    }

    public static string Serialize(AgentProviderConfiguration config) =>
        JsonSerializer.Serialize(config, JsonOptions);

    /// <summary>
    /// A non-reversible 16-hex-char SHA-256-derived fingerprint. Never exposes any part of the key
    /// itself - only lets a caller verify two values are the same key (e.g. confirming a rotation
    /// actually changed it).
    /// </summary>
    public static string ComputeFingerprint(string apiKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record AgentProviderPublicSettings(
    string? Model,
    string? Endpoint,
    bool HasApiKey,
    bool IsDefault = false,
    int? MaxTokens = null,
    string? FallbackProviderAlias = null,
    bool EnablePromptAudit = false,
    string? ApiKeyFingerprint = null,
    DateTime? ApiKeyUpdatedAtUtc = null);
