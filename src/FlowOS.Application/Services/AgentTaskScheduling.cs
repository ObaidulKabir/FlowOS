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

        if (!DecisionPacketFactory.IsWaitingStep(step))
            return Array.Empty<AgentTaskEnqueueRequest>();

        // ADR-003: AgentTask steps implicitly have Actor = Agent; no extra IsAgentHandled check needed.
        // Legacy HumanTask/Command steps still require an explicit Agent/Either actor.
        var isAgentTaskStep = step!.StepType == WorkflowStepType.AgentTask;
        if (!isAgentTaskStep && !StepActor.IsAgentHandled(step.Actor))
            return Array.Empty<AgentTaskEnqueueRequest>();

        string requestedAgentId;
        string objective;

        if (isAgentTaskStep)
        {
            // AgentTask: requestedAgentId encodes the team + slot so the coordinator can resolve
            // the actual agent at dispatch time via IAgentTeamRepository.
            // Format: "team:<teamId>/<slotRole>" or "team:<teamId>" when no slot role is specified.
            // Fallback: if no team is configured, treat like a legacy agent step.
            if (step.AgentTeamId.HasValue)
            {
                var slotPart = string.IsNullOrWhiteSpace(step.AgentTeamSlotRole)
                    ? string.Empty
                    : $"/{step.AgentTeamSlotRole.Trim()}";
                requestedAgentId = $"team:{step.AgentTeamId.Value:N}{slotPart}";
            }
            else
            {
                requestedAgentId = string.IsNullOrWhiteSpace(step.AgentProvider)
                    ? "RiskAnalysisAgent"
                    : step.AgentProvider.Trim();
            }

            objective = string.IsNullOrWhiteSpace(step.DecisionGuideline)
                ? "Complete the agent task and propose the next legal workflow event."
                : step.DecisionGuideline.Trim();
        }
        else
        {
            requestedAgentId = string.IsNullOrWhiteSpace(step.AgentProvider)
                ? "RiskAnalysisAgent"
                : step.AgentProvider.Trim();
            objective = string.IsNullOrWhiteSpace(step.DecisionGuideline)
                ? "Decide the next legal workflow event."
                : step.DecisionGuideline.Trim();
        }

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
