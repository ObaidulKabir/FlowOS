using FlowOS.Agents.Abstractions;

namespace FlowOS.Application.Common.Interfaces;

public interface IWorkflowAgentFactory
{
    Task<IWorkflowAgent> CreateAsync(
        DecisionPacket packet,
        string requestedAgentId,
        CancellationToken cancellationToken = default);
}
