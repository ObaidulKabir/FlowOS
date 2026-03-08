using System.Threading.Tasks;

namespace FlowOS.Agents.Abstractions;

/// <summary>
/// Specialized agent that understands workflow definitions and can reason about state transitions.
/// </summary>
public interface IWorkflowAgent : IAgent
{
    // A workflow agent might have specific methods to access definition metadata
    // For now, it just marks the capability to reason about workflows.
}
