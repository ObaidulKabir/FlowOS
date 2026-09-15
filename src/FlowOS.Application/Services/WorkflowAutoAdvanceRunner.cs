using FlowOS.Domain.Entities;
using FlowOS.Events.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;

namespace FlowOS.Application.Services;

public sealed record WorkflowAutoAdvanceTrace(
    string EventType,
    string FromStepId,
    string ToStepId,
    IReadOnlyList<string> ActiveStepIds,
    string FromState,
    string ToState,
    bool IsAllowed,
    string Message);

public static class WorkflowAutoAdvanceRunner
{
    public static IReadOnlyList<WorkflowAutoAdvanceTrace> Run(
        WorkflowEngine engine,
        WorkflowInstance instance,
        WorkflowDefinition definition,
        Guid tenantId,
        FlowOS.StateMachines.Models.ExecutionContext context,
        StateMachineDefinition? stateMachineDefinition = null,
        int limit = 10)
    {
        var trace = new List<WorkflowAutoAdvanceTrace>();
        var remaining = Math.Clamp(limit, 0, 100);

        while (remaining > 0)
        {
            var activeSteps = instance.ActiveStepIds.Count > 0
                ? instance.ActiveStepIds.ToList()
                : string.IsNullOrEmpty(instance.CurrentStepId)
                    ? new List<string>()
                    : new List<string> { instance.CurrentStepId };

            var advanced = false;
            foreach (var stepId in activeSteps)
            {
                var step = definition.Steps.FirstOrDefault(candidate => candidate.StepId == stepId);
                if (step == null ||
                    !step.NextSteps.ContainsKey("Default") ||
                    step.StepType is WorkflowStepType.HumanTask or WorkflowStepType.Timer or WorkflowStepType.SubWorkflow)
                {
                    continue;
                }

                var fromStep = instance.CurrentStepId;
                var fromState = instance.CurrentState ?? instance.CurrentStepId;
                var result = engine.Advance(
                    instance,
                    definition,
                    new StandardEvent(tenantId, "Default"),
                    context,
                    stateMachineDefinition,
                    fromState);

                trace.Add(new WorkflowAutoAdvanceTrace(
                    "Default",
                    fromStep,
                    instance.CurrentStepId,
                    instance.ActiveStepIds.ToList(),
                    fromState,
                    instance.CurrentState ?? fromState,
                    result.Success,
                    result.Message));

                if (!result.Success)
                {
                    return trace;
                }

                advanced = true;
                break;
            }

            if (!advanced) break;
            remaining--;
        }

        return trace;
    }
}
