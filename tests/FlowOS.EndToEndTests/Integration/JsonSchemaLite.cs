using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FlowOS.EndToEndTests.Integration;

/// <summary>
/// Small JSON Schema checker for the keywords the FlowOS contract schemas use: $ref (local $defs),
/// type, const, required, properties, additionalProperties, items, minimum and the uuid and
/// date-time formats. Keeps the contract tests free of an extra package.
/// </summary>
public static class JsonSchemaLite
{
    public static JsonDocument LoadRepositorySchema(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return JsonDocument.Parse(File.ReadAllText(candidate));
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Schema '{relativePath}' not found above {AppContext.BaseDirectory}.");
    }

    public static List<string> Validate(JsonElement schemaRoot, JsonElement value)
    {
        var errors = new List<string>();
        Check(schemaRoot, schemaRoot, value, "$", errors);
        return errors;
    }

    private static void Check(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()!.Split('/').Last();
            schema = root.GetProperty("$defs").GetProperty(name);
        }

        if (schema.TryGetProperty("const", out var constant) && constant.GetRawText() != value.GetRawText())
            errors.Add($"{path}: expected {constant.GetRawText()}, got {value.GetRawText()}");

        if (schema.TryGetProperty("type", out var type))
        {
            var allowed = type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Select(t => t.GetString()!).ToArray()
                : new[] { type.GetString()! };
            if (!allowed.Any(t => Matches(t, value)))
            {
                errors.Add($"{path}: expected {string.Join("|", allowed)}, got {value.ValueKind}");
                return;
            }
        }

        if (schema.TryGetProperty("format", out var format) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            var ok = format.GetString() switch
            {
                "uuid" => Guid.TryParse(text, out _),
                "date-time" => DateTimeOffset.TryParse(text, out _),
                _ => true
            };
            if (!ok) errors.Add($"{path}: '{text}' is not a valid {format.GetString()}");
        }

        if (schema.TryGetProperty("minimum", out var minimum) && value.ValueKind == JsonValueKind.Number
            && value.GetDecimal() < minimum.GetDecimal())
            errors.Add($"{path}: {value.GetDecimal()} is below the minimum {minimum.GetDecimal()}");

        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = schema.TryGetProperty("properties", out var p) ? p : default;
            if (schema.TryGetProperty("required", out var required))
                foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
                    if (!value.TryGetProperty(name, out _)) errors.Add($"{path}: missing required '{name}'");

            foreach (var member in value.EnumerateObject())
            {
                var childPath = $"{path}.{member.Name}";
                if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(member.Name, out var childSchema))
                    Check(root, childSchema, member.Value, childPath, errors);
                else if (schema.TryGetProperty("additionalProperties", out var additional))
                {
                    if (additional.ValueKind == JsonValueKind.False) errors.Add($"{childPath}: not allowed by the schema");
                    else if (additional.ValueKind == JsonValueKind.Object) Check(root, additional, member.Value, childPath, errors);
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var i = 0;
            foreach (var element in value.EnumerateArray()) Check(root, items, element, $"{path}[{i++}]", errors);
        }
    }

    private static bool Matches(string type, JsonElement value) => type switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false
    };
}
