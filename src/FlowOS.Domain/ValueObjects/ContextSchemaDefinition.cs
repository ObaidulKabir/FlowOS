using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FlowOS.Domain.ValueObjects;

/// <summary>
/// Root declarative specification of Business Context for a workflow class.
/// Defines identity, typed Redis-hash-like fields, reactive computed fields, and access governance.
/// </summary>
public record ContextSchemaDefinition
{
    public string? EntityType { get; init; }
    public string? EntityIdField { get; init; }
    public List<string> CorrelationFields { get; init; } = new();
    public Dictionary<string, ContextFieldDefinition> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ComputedFieldDefinition> Computed { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, FieldAccessDefinition> FieldAccess { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ContextIndexDefinition> Indexes { get; init; } = new();
}

/// <summary>
/// Declarative specification of an individual field in the Business Context hash.
/// </summary>
public record ContextFieldDefinition
{
    public string Type { get; init; } = "string"; // string, number, boolean, datetime, object, array, counter
    public bool Required { get; init; }
    public bool Immutable { get; init; }
    public bool AppendOnly { get; init; }
    public bool System { get; init; }
    public bool Computed { get; init; }
    public object? Default { get; init; }
    public List<string>? Enum { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public string? Auto { get; init; } // e.g. "now", "onEntry:StepId"
}

/// <summary>
/// Reactive / derived field whose value is automatically evaluated whenever dependencies mutate.
/// </summary>
public record ComputedFieldDefinition
{
    public string Expression { get; init; } = string.Empty;
    public List<string> RecomputeOn { get; init; } = new();
}

/// <summary>
/// Field-level access governance declaring which roles may read or write a specific field.
/// </summary>
public record FieldAccessDefinition
{
    public List<string> ReadRoles { get; init; } = new();
    public List<string> WriteRoles { get; init; } = new();
    public List<string> WriteOnSteps { get; init; } = new();
}

/// <summary>
/// Secondary index / sorted-set declaration for cross-instance lookup.
/// </summary>
public record ContextIndexDefinition
{
    public List<string> Fields { get; init; } = new();
    public bool Unique { get; init; }
}

/// <summary>
/// Parser helper that gracefully parses either a rich ContextSchemaDefinition or standard JSON Schema format.
/// </summary>
public static class ContextSchemaParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(string? schemaJson, out ContextSchemaDefinition? definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(schemaJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "ContextSchema", StringComparison.OrdinalIgnoreCase))
                    {
                        definition = JsonSerializer.Deserialize<ContextSchemaDefinition>(prop.Value.GetRawText(), JsonOptions);
                        return definition != null;
                    }
                }

                // Check if it's already a ContextSchemaDefinition with "fields" or "entityType"
                foreach (var prop in root.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "fields", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(prop.Name, "entityType", StringComparison.OrdinalIgnoreCase))
                    {
                        definition = JsonSerializer.Deserialize<ContextSchemaDefinition>(schemaJson, JsonOptions);
                        return definition != null;
                    }
                }
            }

            // If it's standard JSON schema (with "properties"), map properties to ContextFieldDefinitions
            if (root.ValueKind == JsonValueKind.Object &&
                (root.TryGetProperty("properties", out var props) || root.TryGetProperty("Properties", out props)))
            {
                var fields = new Dictionary<string, ContextFieldDefinition>(StringComparer.OrdinalIgnoreCase);
                var requiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (root.TryGetProperty("required", out var reqArray) && reqArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in reqArray.EnumerateArray())
                    {
                        var reqStr = item.GetString();
                        if (!string.IsNullOrEmpty(reqStr)) requiredSet.Add(reqStr);
                    }
                }

                foreach (var prop in props.EnumerateObject())
                {
                    var propType = "string";
                    if (prop.Value.TryGetProperty("type", out var typeElem))
                    {
                        propType = typeElem.GetString() ?? "string";
                    }

                    fields[prop.Name] = new ContextFieldDefinition
                    {
                        Type = propType,
                        Required = requiredSet.Contains(prop.Name)
                    };
                }

                definition = new ContextSchemaDefinition
                {
                    Fields = fields
                };
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
