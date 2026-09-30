using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowOS.Core.Common.Models;

/// <summary>
/// Structured tool configuration stored in tenant AI Context (bindingType: 'action').
/// Maps a business tool alias to an underlying plugin or connector, with parameters schema,
/// security side-effect level, capability gate, and prefetch rules.
/// </summary>
public sealed class AgentToolConfiguration
{
    public string? Description { get; set; }
    [JsonConverter(typeof(RawJsonOrStringConverter))]
    public string? ParametersSchema { get; set; }
    public string SideEffect { get; set; } = "none";
    public string? RequiredCapability { get; set; }
    public bool Prefetch { get; set; }
    public Dictionary<string, string>? ArgumentMapping { get; set; }

    public static AgentToolConfiguration? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<AgentToolConfiguration>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string Merge(string? existingJson, string? incomingJson)
    {
        var existing = Parse(existingJson) ?? new AgentToolConfiguration();
        var incoming = Parse(incomingJson);
        if (incoming == null) return existingJson ?? "{}";

        if (!string.IsNullOrWhiteSpace(incoming.Description))
            existing.Description = incoming.Description.Trim();

        if (!string.IsNullOrWhiteSpace(incoming.ParametersSchema))
            existing.ParametersSchema = incoming.ParametersSchema.Trim();

        if (!string.IsNullOrWhiteSpace(incoming.SideEffect))
            existing.SideEffect = incoming.SideEffect.Trim().ToLowerInvariant();

        if (incoming.RequiredCapability != null)
            existing.RequiredCapability = string.IsNullOrWhiteSpace(incoming.RequiredCapability)
                ? null
                : incoming.RequiredCapability.Trim();

        existing.Prefetch = incoming.Prefetch;

        if (incoming.ArgumentMapping != null)
        {
            existing.ArgumentMapping = new Dictionary<string, string>(incoming.ArgumentMapping, StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Serialize(existing, JsonOptions);
    }

    public static AgentToolPublicSettings Public(string? json)
    {
        var parsed = Parse(json) ?? new AgentToolConfiguration();

        object? schemaObj = null;
        if (!string.IsNullOrWhiteSpace(parsed.ParametersSchema))
        {
            try
            {
                using var doc = JsonDocument.Parse(parsed.ParametersSchema);
                schemaObj = JsonSerializer.Deserialize<object>(parsed.ParametersSchema, JsonOptions);
            }
            catch
            {
                schemaObj = parsed.ParametersSchema;
            }
        }

        return new AgentToolPublicSettings(
            parsed.Description,
            schemaObj,
            string.IsNullOrWhiteSpace(parsed.SideEffect) ? "none" : parsed.SideEffect,
            parsed.RequiredCapability,
            parsed.Prefetch,
            parsed.ArgumentMapping);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record AgentToolPublicSettings(
    string? Description,
    object? ParametersSchema,
    string SideEffect,
    string? RequiredCapability,
    bool Prefetch,
    IReadOnlyDictionary<string, string>? ArgumentMapping);

public sealed class RawJsonOrStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString();
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return doc.RootElement.GetRawText();
        }
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}
