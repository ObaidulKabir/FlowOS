using System.Collections.Generic;

namespace FlowOS.Security.Policies;

public record PolicyContext
{
    public string TenantId { get; init; } = string.Empty;
    public string ActorId { get; init; } = string.Empty;
    public List<string> Roles { get; init; } = new();
    public string CommandType { get; init; } = string.Empty;
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    public string? WorkflowId { get; init; }
    public string? CurrentStep { get; init; }
}
