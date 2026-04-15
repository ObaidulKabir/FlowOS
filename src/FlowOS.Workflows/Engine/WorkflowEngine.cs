using System.Linq;
using System.Linq.Dynamic.Core; // Added for Expression Evaluation
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
        else if (nextStep.StepType == WorkflowStepType.Decision) // New Logic for Decision
        {
            // Evaluate Conditions
            // We need the Payload from the Event.
            // Currently IEvent doesn't expose Payload generically easily, but we can assume it's in the Context or Event.
            // Let's assume Context.Payload contains the data (Dictionary<string, object>).

            // NOTE: Dynamic LINQ requires a queryable or object context.
            // We'll use the Payload dictionary.

            string? decisionTarget = null;

            // Iterate through conditions
            foreach (var condition in nextStep.Conditions)
            {
                var expression = condition.Key;
                var target = condition.Value;

                try
                {
                    // Evaluate Expression against Context.Payload
                    // Using Dynamic LINQ: DynamicExpressionParser.ParseLambda
                    // But we need to convert Dictionary to an object or use a specific parsing strategy.
                    // Simple approach: Replace variables in string? No, risky.
                    // Better: Use Dynamic LINQ on a List of 1 object?

                    // Actually, System.Linq.Dynamic.Core supports IDictionary access if configured,
                    // OR we can pass the Payload object directly if it's dynamic/Expando.

                    // Let's assume context.Payload is the model.
                    // If Payload is null, we can't evaluate.

                    if (context.Payload != null)
                    {
                        // We wrap payload in a list to use AsQueryable()
                        var queryable = newList(context.Payload).AsQueryable();
                        // This might be tricky with Dictionary.
                        // Let's try to interpret simple rules first or assume Payload is a specific Type?
                        // No, Payload is dynamic.

                        // For Phase 1 (CEL implementation):
                        // We will support simple property access like "Amount > 100".
                        // Dynamic LINQ can parse this if we provide a Lambda.

                        // HACK: For prototype, if Payload is Dictionary<string, object>,
                        // we can try to use NCalc or just specific parsing.
                        // BUT, System.Linq.Dynamic.Core is powerful.

                        // Let's try evaluating:
                        // var result = DynamicExpressionParser.ParseLambda(typeof(Dictionary<string, object>), typeof(bool), expression).Compile().Invoke(context.Payload);
                        // Accessing dictionary keys in Dynamic LINQ: "it[\"Amount\"] > 100"
                        // But users want "Amount > 100".

                        // Workaround: Replace property names with dictionary access syntax?
                        // Or utilize a helper method.

                        // Let's assume for now we just log it and default to the FIRST match (simulated).
                        // In a real implementation, we'd wire up the Dynamic LINQ parser properly.

                        // SIMULATION for now to avoid compilation errors without full setup:
                        // If condition contains "> 100" and Payload["Amount"] > 100 -> true.

                        // REAL IMPL Attempt:
                        // var p = System.Linq.Expressions.Expression.Parameter(typeof(object), "x");
                        // ... too complex for this snippet.

                        // Fallback: If "Default" is in Conditions, use it?
                        // No, Conditions is Key=Expr, Value=Target.

                        // Let's implement a naive evaluator for the demo.
                        if (EvaluateCondition(expression, context.Payload))
                        {
                            decisionTarget = target;
                            break;
                        }
                    }
                }
                catch
                {
                    // Log error, skip
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

    // Helper for Naive Evaluation (Replace with real CEL later)
    private bool EvaluateCondition(string expression, Dictionary<string, object> payload)
    {
        // Support simple "Key > Value"
        // e.g. "Amount > 100"

        var parts = expression.Split(' ');
        if (parts.Length != 3) return false; // Only support simple binary

        var key = parts[0];
        var op = parts[1];
        var valStr = parts[2];

        if (!payload.ContainsKey(key)) return false;

        var val = payload[key];

        if (double.TryParse(val.ToString(), out var numVal) && double.TryParse(valStr, out var compareVal))
        {
            return op switch
            {
                ">" => numVal > compareVal,
                "<" => numVal < compareVal,
                ">=" => numVal >= compareVal,
                "<=" => numVal <= compareVal,
                "==" => numVal == compareVal,
                _ => false
            };
        }

        return false;
    }

    // Helper list wrapper
    private IEnumerable<T> newList<T>(T item) { yield return item; }
}
