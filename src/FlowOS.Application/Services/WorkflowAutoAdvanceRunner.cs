using FlowOS.Domain.Entities;
using FlowOS.Events.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;

namespace FlowOS.Application.Services;

public sealed record WorkflowAutoAdvanceTrace(
    string EventType,
    string? FromStepId,
    string? ToStepId,
    IReadOnlyList<string> ActiveStepIds,
    string FromState,
    string ToState,
    bool IsAllowed,
    string Message,
    IReadOnlyList<string> DepartedStepIds,
    IReadOnlyList<string> EnteredStepIds);

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

                var activeBefore = instance.ActiveStepIds.ToList();
                var currentBefore = instance.CurrentStepId;
                
                var fromStep = stepId;
                var fromState = instance.CurrentState ?? fromStep;
                var result = engine.Advance(
                    instance,
                    definition,
                    new StandardEvent(tenantId, "Default"),
                    context,
                    stateMachineDefinition,
                    fromState);

                var activeAfter = instance.ActiveStepIds.ToList();
                var currentAfter = instance.CurrentStepId;

                var departedSteps = new List<string>();
                if (activeBefore.Count > 0)
                {
                    departedSteps.AddRange(activeBefore.Where(s => !activeAfter.Contains(s)));
                }
                else if (!string.IsNullOrEmpty(currentBefore) && currentBefore != currentAfter)
                {
                    departedSteps.Add(currentBefore);
                }

                var enteredSteps = new List<string>();
                if (activeAfter.Count > 0)
                {
                    enteredSteps.AddRange(activeAfter.Where(s => !activeBefore.Contains(s)));
                }
                else if (!string.IsNullOrEmpty(currentAfter) && currentAfter != currentBefore)
                {
                    enteredSteps.Add(currentAfter);
                }

                trace.Add(new WorkflowAutoAdvanceTrace(
                    "Default",
                    fromStep,
                    instance.CurrentStepId,
                    instance.ActiveStepIds.ToList(),
                    fromState,
                    instance.CurrentState ?? fromState,
                    result.Success,
                    result.Message,
                    departedSteps,
                    enteredSteps));

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
