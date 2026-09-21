using FlowOS.Agents.Abstractions;

namespace FlowOS.Application.Common.Interfaces;

public sealed record AgentTaskRunResult(
    bool Ran,
    bool AutoCommitted,
    string? SkipReason,
    string? ParkReason,
    string AgentId,
    AgentResult? AgentResult,
    DecisionPacket? Packet,
    string? RuntimeIdentifier = null,
    string? ProviderAlias = null,
    string? ProviderName = null,
    string? Model = null,
    Guid? ExecutionId = null,
    Guid? JobId = null);

public sealed record AgentTaskExecutionContext(
    Guid? JobId = null,
    string? Claimant = null,
    int? Attempt = null,
    string? ExpectedStepId = null,
    Guid? ExecutionId = null);

public interface IAgentTaskRunner
{
    Task<AgentTaskRunResult> SuggestAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? agentId = null,
        string? objective = null,
        CancellationToken cancellationToken = default,
        AgentTaskExecutionContext? executionContext = null);

    Task<AgentTaskRunResult> TryRunForCurrentStepAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? agentId = null,
        CancellationToken cancellationToken = default,
        AgentTaskExecutionContext? executionContext = null);
}
