using FlowOS.Agents.Abstractions;

namespace FlowOS.Application.Common.Interfaces;

public sealed record AgentTaskRunResult(
    bool Ran,
    bool AutoCommitted,
    string? SkipReason,
    string? ParkReason,
    string AgentId,
    AgentResult? AgentResult,
    DecisionPacket? Packet);

public interface IAgentTaskRunner
{
    Task<AgentTaskRunResult> SuggestAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string agentId,
        string? objective = null,
        CancellationToken cancellationToken = default);

    Task<AgentTaskRunResult> TryRunForCurrentStepAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? agentId = null,
        CancellationToken cancellationToken = default);
}
