using System.Collections.Generic;

namespace FlowOS.StateMachines.Models;

// Placeholder for future RBAC context
public class ExecutionContext
{
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    // The payload data being processed (e.g., Expense Amount)
    public Dictionary<string, object> Payload { get; set; } = new();

    // Optional per-tenant alias map: blueprint decision provider -> concrete plugin provider.
    public Dictionary<string, string> DecisionProviderBindings { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
}
