namespace FlowOS.Application.Common.Interfaces;

public sealed record ExternalAIAgentExecutedStep(
    string StepId,
    int StepIndex,
    string ToolName,
    string Status,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    string? ErrorMessage,
    string? ResultSnapshotJson);

public sealed record ExternalAIAgentExecutionResult(
    Guid PlanId,
    Guid TenantId,
    bool Success,
    IReadOnlyList<ExternalAIAgentExecutedStep> StepResults);

public interface IExternalAIAgentExecutor
{
    Task<ExternalAIAgentExecutionResult> ExecutePlanAsync(
        Guid planId,
        Guid? requestedTenantId = null,
        CancellationToken cancellationToken = default);

    Task<ExternalAIAgentExecutionResult> ResumePlanAsync(
        Guid planId,
        string? fromStepId = null,
        Guid? requestedTenantId = null,
        CancellationToken cancellationToken = default);
}
