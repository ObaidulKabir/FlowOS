using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Enums;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;

namespace FlowOS.Application.Services;

public static class AgentTaskScheduling
{
    public static IReadOnlyList<AgentTaskEnqueueRequest> ForCurrentWaitingStep(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        AgentTaskSource source)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(definition);

        if (instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed ||
            string.IsNullOrWhiteSpace(instance.CurrentStepId))
        {
            return Array.Empty<AgentTaskEnqueueRequest>();
        }

        var step = definition.Steps.FirstOrDefault(candidate =>
            string.Equals(
                candidate.StepId,
                instance.CurrentStepId,
                StringComparison.OrdinalIgnoreCase));
        if (!DecisionPacketFactory.IsWaitingStep(step) ||
            !StepActor.IsAgentHandled(step!.Actor))
        {
            return Array.Empty<AgentTaskEnqueueRequest>();
        }

        var requestedAgentId = string.IsNullOrWhiteSpace(step.AgentProvider)
            ? "RiskAnalysisAgent"
            : step.AgentProvider.Trim();
        var objective = string.IsNullOrWhiteSpace(step.DecisionGuideline)
            ? "Decide the next legal workflow event."
            : step.DecisionGuideline.Trim();

        return
        [
            new AgentTaskEnqueueRequest(
                instance.TenantId,
                instance.Id,
                step.StepId,
                requestedAgentId,
                objective,
                AllowAutoCommit: true,
                RequireAgentActor: true,
                source)
        ];
    }
}
