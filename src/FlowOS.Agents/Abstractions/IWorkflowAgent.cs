using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Agents.Abstractions;

/// <summary>
/// Specialized agent that understands workflow definitions and can reason about state transitions.
/// </summary>
public interface IWorkflowAgent : IAgent
{
    Task<AgentResult> ExecuteAsync(DecisionPacket packet, CancellationToken cancellationToken = default) =>
        ExecuteAsync(AgentContext.FromPacket(packet), cancellationToken);
}
