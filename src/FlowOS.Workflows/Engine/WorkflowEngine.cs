using System.Linq;
using System.Collections.Generic;
using FlowOS.Domain.Entities;
using FlowOS.Events.Abstractions;
using FlowOS.StateMachines.Engine;
using FlowOS.StateMachines.Models; // Reusing ExecutionContext
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;

namespace FlowOS.Workflows.Engine;

public class WorkflowEngine : IWorkflowEngine
{
    private readonly StateMachineEngine _stateMachineEngine;

    public WorkflowEngine(StateMachineEngine stateMachineEngine)
    {
        _stateMachineEngine = stateMachineEngine
            ?? throw new ArgumentNullException(nameof(stateMachineEngine));
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
        //picking the current step from the instance's CurrentStepId,
        //which is set to "Start" when the workflow is initiated.
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

        // 6. Execute Step Logic
        if (nextStep.StepType == WorkflowStepType.HumanTask)
        {
            instance.AdvanceTo(nextStepId);
            instance.Wait(); // Pause for human
            return WorkflowAdvanceResult.Waiting("Waiting for human task completion.");
        }
        else if (nextStep.StepType == WorkflowStepType.Timer)
        {
            instance.AdvanceTo(nextStepId);
            instance.Wait(); // Pause for timer
            return WorkflowAdvanceResult.Waiting("Waiting for timer trigger.");
        }
        else if (nextStep.StepType == WorkflowStepType.Decision)
        {
            // Evaluate each condition expression against the execution context payload.
            // The first condition that evaluates to true determines the target step.
            string? decisionTarget = null;

            foreach (var condition in nextStep.Conditions)
            {
                var expression = condition.Key;
                var target = condition.Value;

                try
                {
                    if (context.Payload != null && EvaluateCondition(expression, context.Payload))
                    {
                        decisionTarget = target;
                        break;
                    }
                }
                catch
                {
                    // Skip malformed expressions gracefully
                }
            }

            // If no condition met, look for "Default" key in Conditions?
            if (decisionTarget == null && nextStep.Conditions.ContainsKey("Default"))
            {
                decisionTarget = nextStep.Conditions["Default"];
            }

            if (decisionTarget != null)
            {
                // Recursive Advance!
                // We found where to go, so we advance the instance to this Decision Step (transiently)
                // then immediately advance to the target.
                instance.AdvanceTo(nextStepId); // Record we hit the decision

                // Now verify target exists
                var targetStep = definition.Steps.FirstOrDefault(s => s.StepId == decisionTarget);
                if (targetStep == null)
                    return WorkflowAdvanceResult.Failed($"Decision target '{decisionTarget}' not found.");

                // Move instance to target
                if (decisionTarget == "END")
                {
                    instance.Complete();
                    return WorkflowAdvanceResult.Completed();
                }

                instance.AdvanceTo(decisionTarget);
                return WorkflowAdvanceResult.Advanced(decisionTarget);
            }
            else
            {
                return WorkflowAdvanceResult.Failed($"No condition met in Decision step '{nextStepId}'.");
            }
        }

        // Default Advance
        instance.AdvanceTo(nextStepId);
        return WorkflowAdvanceResult.Advanced(nextStepId);
    }

    // Evaluates complex conditions using System.Linq.Dynamic.Core
    private bool EvaluateCondition(string expression, Dictionary<string, object> payload)
    {
        return ExpressionEvaluator.Evaluate(expression, payload);
    }
}
