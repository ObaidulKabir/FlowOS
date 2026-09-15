using System.Text.Json;

namespace FlowOS.Domain.ValueObjects;

public record WorkflowContextBindingDefinition
{
    public string EntityType { get; init; } = string.Empty;
    public Dictionary<string, string> EventAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RoleOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CapabilityOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> InputMapping { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> EventInputMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonElement> ConditionParameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DecisionProviderOverrides { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? SourcePayloadSchema { get; init; }
    public Dictionary<string, string> EventSourcePayloadSchemas { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
