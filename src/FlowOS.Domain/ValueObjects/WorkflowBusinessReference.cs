namespace FlowOS.Domain.ValueObjects;

public record WorkflowBusinessReference
{
    public string? SourceSystem { get; init; }
    public string? ExternalEntityId { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
