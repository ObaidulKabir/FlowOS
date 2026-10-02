namespace FlowOS.Application.Common.Interfaces;

public sealed record ExternalAIAgentPlannedStep(
    string StepId,
    int StepIndex,
    string ToolName,
    string ToolArgsJson,
    string? Description,
    IReadOnlyList<string> DependsOn);

public sealed record ExternalAIAgentPlanResult(
    Guid PlanId,
    Guid ChangeId,
    Guid TenantId,
    string? AgentProfileId,
    IReadOnlyList<string> ValidationDropped,
    IReadOnlyList<ExternalAIAgentPlannedStep> Steps,
    string RawPlanJson);

public interface IExternalAIAgentPlanner
{
    Task<ExternalAIAgentPlanResult> PlanAsync(
        Guid changeId,
        Guid? requestedTenantId = null,
        string? agentProfileId = null,
        string? objective = null,
        CancellationToken cancellationToken = default);
}
