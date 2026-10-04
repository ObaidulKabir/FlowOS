using System.Text.Json;

namespace FlowOS.Domain.ValueObjects;

public enum ContextOperationType
{
    Set = 0,
    Increment = 1,
    Append = 2,
    DeepMerge = 3,
    SetIfAbsent = 4
}

public record ContextOperation
{
    public ContextOperationType Type { get; init; } = ContextOperationType.Set;
    public string Field { get; init; } = string.Empty;
    public JsonElement Value { get; init; }

    public static ContextOperation Set(string field, JsonElement value) =>
        new() { Type = ContextOperationType.Set, Field = field, Value = value };

    public static ContextOperation Increment(string field, decimal delta) =>
        new() { Type = ContextOperationType.Increment, Field = field, Value = JsonSerializer.SerializeToElement(delta) };

    public static ContextOperation Append(string field, JsonElement item) =>
        new() { Type = ContextOperationType.Append, Field = field, Value = item };

    public static ContextOperation DeepMerge(string field, JsonElement partial) =>
        new() { Type = ContextOperationType.DeepMerge, Field = field, Value = partial };

    public static ContextOperation SetIfAbsent(string field, JsonElement value) =>
        new() { Type = ContextOperationType.SetIfAbsent, Field = field, Value = value };
}
