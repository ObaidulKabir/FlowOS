using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Agents.Abstractions;

public interface IConversationalAgent : IWorkflowAgent
{
    int MaxTurns { get; }

    Task<AgentResult> ExecuteConversationalAsync(
        DecisionPacket packet,
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default);
}
