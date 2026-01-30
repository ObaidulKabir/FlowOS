using System.Linq;
using FlowOS.Domain.Entities;
using FlowOS.Events.Abstractions;
using FlowOS.StateMachines.Engine;
using FlowOS.StateMachines.Models; // Reusing ExecutionContext
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;

namespace FlowOS.Workflows.Engine;

public class WorkflowEngine
{
    private readonly StateMachineEngine _stateMachineEngine;

    public WorkflowEngine()
    {
        _stateMachineEngine = new StateMachineEngine();
    }

    public WorkflowAdvanceResult Advance(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        IEvent domainEvent,
        FlowOS.StateMachines.Models.ExecutionContext context,
        StateMachineDefinition? stateMachineDefinition = null,
        string? currentEntityState = null)
    {
        // 1. Validation
        if (instance.WorkflowDefinitionId != definition.Id)
            return WorkflowAdvanceResult.Failed("Definition mismatch.");

        if (instance.WorkflowVersion != definition.Version)
            return WorkflowAdvanceResult.Failed("Version mismatch.");

        var currentStep = definition.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);
        if (currentStep == null)
            return WorkflowAdvanceResult.Failed($"Current step '{instance.CurrentStepId}' not found in definition.");

        // 2. Check for Transition match (Workflow)
        if (!currentStep.NextSteps.TryGetValue(domainEvent.EventType, out var nextStepId))
        {
            // Event does not trigger a transition from this step
            // This is not an error, just no-op for the workflow
            return WorkflowAdvanceResult.Failed($"No transition defined for event '{domainEvent.EventType}' from step '{instance.CurrentStepId}'.");
        }

        // 3. State Machine Enforcement (The Law)
        if (stateMachineDefinition != null && currentEntityState != null)
        {
            var smResult = _stateMachineEngine.ValidateTransition(
                stateMachineDefinition, 
                currentEntityState, 
                domainEvent, 
                context);

            // We check ResultType. If Ignored, we proceed. If Allowed, we proceed. 
            // If Denied, we fail.
            // Note: ValidateTransition sets IsAllowed=true for Ignored, but false for Denied.
            // So checking !IsAllowed covers Denied.
            
            if (!smResult.IsAllowed)
            {
                return WorkflowAdvanceResult.Failed($"State Machine violation: {smResult.Reason}");
            }
        }

        // 4. Handle End of Workflow
        if (nextStepId == "END") // Convention for end
        {
            instance.Complete();
            return WorkflowAdvanceResult.Completed();
        }

        // 5. Advance
        var nextStep = definition.Steps.FirstOrDefault(s => s.StepId == nextStepId);
        if (nextStep == null)
            return WorkflowAdvanceResult.Failed($"Target step '{nextStepId}' not found.");

        // 6. Execute Step Logic (Minimal for now)
        if (nextStep.StepType == WorkflowStepType.HumanTask)
        {
            instance.AdvanceTo(nextStepId);
            instance.Wait(); // Pause for human
            return WorkflowAdvanceResult.Waiting("Waiting for human task completion.");
        }

        // Default Advance
        instance.AdvanceTo(nextStepId);
        return WorkflowAdvanceResult.Advanced(nextStepId);
    }
}
