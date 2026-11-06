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

    /// <summary>
    /// Tenant integration finding N5: optional sampling temperature passed through to the provider.
    /// When null, adapters omit it and the provider's own default applies. Honored by OpenAI
    /// (including Azure/custom OpenAI-compatible endpoints), Anthropic, and Google.
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// Tenant integration finding N5: optional deterministic sampling seed. Only OpenAI-compatible
    /// providers (openai/azure-openai/custom) honor seed; Anthropic and Google have no equivalent
    /// parameter in their APIs and ignore it.
    /// </summary>
    public int? Seed { get; set; }

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

        var previousEndpoint = existing.Endpoint;
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
        else if (!string.IsNullOrWhiteSpace(incoming.Endpoint) &&
                 !string.IsNullOrWhiteSpace(existing.ApiKey) &&
                 !EndpointsShareHost(previousEndpoint, existing.Endpoint))
        {
            // Tenant integration finding (post-H3/M2/P2 review): repointing the endpoint without
            // supplying a new key kept sending the previously stored key to whatever host was saved -
            // the key is a Bearer token, so moving the endpoint silently redirects where it is sent.
            // Force the key to be re-supplied whenever the endpoint's host actually changes; a path-only
            // or scheme-only edit to the same host is not treated as a move.
            existing.ApiKey = null;
            existing.ApiKeyFingerprint = null;
            existing.ApiKeyUpdatedAtUtc = null;
        }
        existing.IsDefault = incoming.IsDefault;
        existing.MaxTokens = incoming.MaxTokens ?? existing.MaxTokens;
        existing.FallbackProviderAlias = string.IsNullOrWhiteSpace(incoming.FallbackProviderAlias) ? existing.FallbackProviderAlias : incoming.FallbackProviderAlias.Trim();
        existing.EnablePromptAudit = incoming.EnablePromptAudit;
        existing.Temperature = incoming.Temperature ?? existing.Temperature;
        existing.Seed = incoming.Seed ?? existing.Seed;

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
            parsed.ApiKeyUpdatedAtUtc,
            parsed.Temperature,
            parsed.Seed);
    }

    public static string Serialize(AgentProviderConfiguration config) =>
        JsonSerializer.Serialize(config, JsonOptions);

    /// <summary>
    /// True when there is no earlier endpoint to compare against (nothing to have moved from), or both
    /// endpoints parse to the same host and port. An unparseable new endpoint is treated as a move
    /// (returns false) rather than silently kept.
    /// </summary>
    private static bool EndpointsShareHost(string? previousEndpoint, string? newEndpoint)
    {
        if (string.IsNullOrWhiteSpace(previousEndpoint))
            return true;

        if (Uri.TryCreate(previousEndpoint, UriKind.Absolute, out var previousUri) &&
            Uri.TryCreate(newEndpoint, UriKind.Absolute, out var newUri))
        {
            return string.Equals(previousUri.Authority, newUri.Authority, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(previousEndpoint.Trim(), newEndpoint?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

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
    DateTime? ApiKeyUpdatedAtUtc = null,
    double? Temperature = null,
    int? Seed = null);
