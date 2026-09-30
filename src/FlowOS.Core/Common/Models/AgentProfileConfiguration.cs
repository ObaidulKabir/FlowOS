using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowOS.Core.Common.Models;

/// <summary>
/// Composite agent persona stored in tenant AI Context (bindingType: 'profile').
/// Bundles role, prompt, provider, and curated business tools into a cohesive Agent.
/// </summary>
public sealed class AgentProfileConfiguration
{
    public string? Role { get; set; }
    public string? Description { get; set; }
    public string? ProviderAlias { get; set; }
    public string? PromptAlias { get; set; }
    public List<string> ToolAliases { get; set; } = new();
    public double? AutoCommitThreshold { get; set; }
    public List<string>? AllowedEvents { get; set; }

    public static AgentProfileConfiguration? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<AgentProfileConfiguration>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string Merge(string? existingJson, string? incomingJson)
    {
        var existing = Parse(existingJson) ?? new AgentProfileConfiguration();
        var incoming = Parse(incomingJson);
        if (incoming == null) return existingJson ?? "{}";

        existing.Role = string.IsNullOrWhiteSpace(incoming.Role) ? existing.Role : incoming.Role.Trim();
        existing.Description = string.IsNullOrWhiteSpace(incoming.Description) ? existing.Description : incoming.Description.Trim();
        existing.ProviderAlias = string.IsNullOrWhiteSpace(incoming.ProviderAlias) ? existing.ProviderAlias : incoming.ProviderAlias.Trim();
        existing.PromptAlias = string.IsNullOrWhiteSpace(incoming.PromptAlias) ? existing.PromptAlias : incoming.PromptAlias.Trim();

        if (incoming.ToolAliases is { Count: > 0 })
        {
            existing.ToolAliases = incoming.ToolAliases
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (incoming.AutoCommitThreshold.HasValue)
        {
            existing.AutoCommitThreshold = incoming.AutoCommitThreshold.Value;
        }

        if (incoming.AllowedEvents != null)
        {
            existing.AllowedEvents = incoming.AllowedEvents
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return JsonSerializer.Serialize(existing, JsonOptions);
    }

    public static AgentProfilePublicSettings Public(string? json)
    {
        var parsed = Parse(json) ?? new AgentProfileConfiguration();
        return new AgentProfilePublicSettings(
            parsed.Role,
            parsed.Description,
            parsed.ProviderAlias,
            parsed.PromptAlias,
            parsed.ToolAliases,
            parsed.AutoCommitThreshold,
            parsed.AllowedEvents);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record AgentProfilePublicSettings(
    string? Role,
    string? Description,
    string? ProviderAlias,
    string? PromptAlias,
    IReadOnlyList<string> ToolAliases,
    double? AutoCommitThreshold,
    IReadOnlyList<string>? AllowedEvents);
