using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowOS.Core.Common.Models;

/// <summary>
/// Tenant-owned prompt text stored on a <c>prompt</c> plugin binding.
/// Users create and edit this independently of the workflow template.
/// </summary>
public sealed class AgentPromptConfiguration
{
    public string? Title { get; set; }
    public string? System { get; set; }
    public string? Instructions { get; set; }
    public string? Text { get; set; }

    public string? Body =>
        !string.IsNullOrWhiteSpace(Instructions) ? Instructions : Text;

    public static AgentPromptConfiguration? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<AgentPromptConfiguration>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string Merge(string? existingJson, string? incomingJson)
    {
        var existing = Parse(existingJson) ?? new AgentPromptConfiguration();
        var incoming = Parse(incomingJson);
        if (incoming == null) return existingJson ?? "{}";

        existing.Title = string.IsNullOrWhiteSpace(incoming.Title) ? existing.Title : incoming.Title.Trim();
        existing.System = string.IsNullOrWhiteSpace(incoming.System) ? existing.System : incoming.System.Trim();
        var body = incoming.Body;
        if (!string.IsNullOrWhiteSpace(body))
        {
            existing.Instructions = body.Trim();
            existing.Text = null;
        }

        return JsonSerializer.Serialize(existing, JsonOptions);
    }

    public static AgentPromptConfiguration Public(string? json)
    {
        var parsed = Parse(json) ?? new AgentPromptConfiguration();
        return new AgentPromptConfiguration
        {
            Title = parsed.Title,
            System = parsed.System,
            Instructions = parsed.Body
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
