using System.Linq;
using System.Collections.Generic;
using FlowOS.Domain.Entities;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Events.Abstractions;
using FlowOS.StateMachines.Engine;
using FlowOS.StateMachines.Models; // Reusing ExecutionContext
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;

namespace FlowOS.Workflows.Engine;

public class WorkflowEngine : IWorkflowEngine
{
    private readonly StateMachineEngine _stateMachineEngine;
    private readonly IPolicyDecisionPluginRegistry? _decisionPluginRegistry;

    public WorkflowEngine(
        StateMachineEngine stateMachineEngine,
        IPolicyDecisionPluginRegistry? decisionPluginRegistry = null)
    {
        _stateMachineEngine = stateMachineEngine
            ?? throw new ArgumentNullException(nameof(stateMachineEngine));
        _decisionPluginRegistry = decisionPluginRegistry;
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
        // 1. Locate current step (supports parallel tokens in ActiveStepIds)
        WorkflowStepDefinition? currentStep = null;

        if (instance.ActiveStepIds != null && instance.ActiveStepIds.Count > 1)
        {
            // Disambiguate: select active parallel branch that responds to this event
            currentStep = definition.Steps.FirstOrDefault(s => instance.ActiveStepIds.Contains(s.StepId) && s.NextSteps.ContainsKey(domainEvent.EventType));
        }

        if (currentStep == null)
        {
            currentStep = definition.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);
        }

        if (currentStep == null)
            return WorkflowAdvanceResult.Failed($"Current step '{instance.CurrentStepId}' not found in definition.");

        // 2. Check for Transition match (Workflow)
        if (!currentStep.NextSteps.TryGetValue(domainEvent.EventType, out var nextStepId))
        {
            // Event does not trigger a transition from this step
            return WorkflowAdvanceResult.Failed($"No transition defined for event '{domainEvent.EventType}' from step '{currentStep.StepId}'.");
        }

        // 3. State Machine Enforcement (The Law)
        if (stateMachineDefinition != null && currentEntityState != null)
        {
            var smResult = _stateMachineEngine.ValidateTransition(
                stateMachineDefinition,
                currentEntityState,
                domainEvent,
                context);

            if (!smResult.IsAllowed)
            {
                return WorkflowAdvanceResult.Failed($"State Machine violation: {smResult.Reason}");
            }

            if (smResult.MatchedTransition != null)
            {
                instance.SetCurrentState(smResult.MatchedTransition.ToState);
            }
        }

        // 4. Handle End of Workflow
        if (nextStepId == "END")
        {
            if (instance.ActiveStepIds != null && instance.ActiveStepIds.Count > 1)
            {
                instance.CompleteBranch(currentStep.StepId);
                if (instance.ActiveStepIds.Count == 0)
                {
                    instance.Complete();
                    return WorkflowAdvanceResult.Completed();
                }
                instance.Wait();
                return WorkflowAdvanceResult.Waiting($"Branch '{currentStep.StepId}' ended. Remaining parallel branch(es): {string.Join(", ", instance.ActiveStepIds)}.");
            }

            instance.Complete();
            return WorkflowAdvanceResult.Completed();
        }

        // 5. Advance
        var nextStep = definition.Steps.FirstOrDefault(s => s.StepId == nextStepId);
        if (nextStep == null)
            return WorkflowAdvanceResult.Failed($"Target step '{nextStepId}' not found.");

        // 6. Execute Step Logic

        // --- FORK STEP ---
        if (nextStep.StepType == WorkflowStepType.Fork)
        {
            var forkTargets = (nextStep.Branches != null && nextStep.Branches.Any())
                ? nextStep.Branches
                : nextStep.NextSteps.Values.Distinct().ToList();

            if (forkTargets.Count < 2)
                return WorkflowAdvanceResult.Failed($"Fork step '{nextStepId}' must declare at least 2 distinct branch targets.");

            instance.ForkTo(forkTargets);

            bool anyWaiting = false;
            foreach (var targetId in forkTargets)
            {
                var targetStep = definition.Steps.FirstOrDefault(s => s.StepId == targetId);
                if (targetStep != null && (targetStep.StepType == WorkflowStepType.HumanTask || targetStep.StepType == WorkflowStepType.Timer || targetStep.StepType == WorkflowStepType.SubWorkflow))
                {
                    anyWaiting = true;
                }
            }

            if (anyWaiting)
            {
                instance.Wait();
                return WorkflowAdvanceResult.Waiting($"Forked into {forkTargets.Count} parallel branches: {string.Join(", ", forkTargets)} (waiting for branch tasks).");
            }

            return WorkflowAdvanceResult.Advanced(instance.CurrentStepId);
        }

        // --- JOIN STEP ---
        if (nextStep.StepType == WorkflowStepType.Join)
        {
            var policy = !string.IsNullOrWhiteSpace(nextStep.JoinPolicy) ? nextStep.JoinPolicy : "WaitAll";
            var inbounds = (nextStep.InboundSteps != null && nextStep.InboundSteps.Any())
                ? nextStep.InboundSteps
                : new List<string>();

            // Complete the current branch
            instance.CompleteBranch(currentStep.StepId);

            bool joinSatisfied = false;
            if (policy.Equals("WaitAny", StringComparison.OrdinalIgnoreCase))
            {
                joinSatisfied = true;
            }
            else
            {
                // WaitAll: every inbound step must be in CompletedParallelStepIds
                joinSatisfied = inbounds.All(s => instance.CompletedParallelStepIds.Contains(s));
            }

            if (joinSatisfied)
            {
                string? continuationStepId = null;
                if (nextStep.NextSteps.TryGetValue("Default", out var defTarget))
                {
                    continuationStepId = defTarget;
                }
                else if (nextStep.NextSteps.Any())
                {
                    continuationStepId = nextStep.NextSteps.First().Value;
                }

                if (string.IsNullOrEmpty(continuationStepId))
                {
                    return WorkflowAdvanceResult.Failed($"Join step '{nextStepId}' has no exit path.");
                }

                if (continuationStepId == "END")
                {
                    instance.Complete();
                    return WorkflowAdvanceResult.Completed();
                }

                var contStep = definition.Steps.FirstOrDefault(s => s.StepId == continuationStepId);
                if (contStep == null)
                    return WorkflowAdvanceResult.Failed($"Join continuation target '{continuationStepId}' not found.");

                instance.JoinTo(continuationStepId);

                if (contStep.StepType == WorkflowStepType.HumanTask || contStep.StepType == WorkflowStepType.Timer || contStep.StepType == WorkflowStepType.SubWorkflow)
                {
                    instance.Wait();
                    return WorkflowAdvanceResult.Waiting($"Parallel branches converged at join '{nextStepId}'. Waiting at '{continuationStepId}'.");
                }

                return WorkflowAdvanceResult.Advanced(continuationStepId);
            }
            else
            {
                instance.Wait();
                var pending = inbounds.Where(s => !instance.CompletedParallelStepIds.Contains(s));
                return WorkflowAdvanceResult.Waiting($"Branch '{currentStep.StepId}' arrived at join '{nextStepId}'. Waiting for remaining branch(es): {string.Join(", ", pending)}.");
            }
        }

        // --- PARALLEL BRANCH CONTINUATION ---
        if (instance.ActiveStepIds != null && instance.ActiveStepIds.Count > 1)
        {
            instance.CompleteBranch(currentStep.StepId, nextStepId);

            if (nextStep.StepType == WorkflowStepType.HumanTask || nextStep.StepType == WorkflowStepType.Timer || nextStep.StepType == WorkflowStepType.SubWorkflow)
            {
                instance.Wait();
                return WorkflowAdvanceResult.Waiting($"Branch '{currentStep.StepId}' advanced to '{nextStepId}' (waiting).");
            }

            return WorkflowAdvanceResult.Advanced(nextStepId);
        }

        // --- HUMAN TASK ---
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
        else if (nextStep.StepType == WorkflowStepType.SubWorkflow)
        {
            instance.AdvanceTo(nextStepId);
            instance.Wait(); // Pause while child workflow executes.
            return WorkflowAdvanceResult.Waiting("Waiting for subworkflow completion.");
        }
        else if (nextStep.StepType == WorkflowStepType.Decision)
        {
            string? decisionTarget = null;
            var providerName = nextStep.DecisionProvider?.Trim();
            if (!string.IsNullOrWhiteSpace(providerName) &&
                context.DecisionProviderBindings != null &&
                context.DecisionProviderBindings.TryGetValue(providerName, out var mappedProviderName) &&
                !string.IsNullOrWhiteSpace(mappedProviderName))
            {
                providerName = mappedProviderName;
            }
            if (!string.IsNullOrWhiteSpace(providerName) &&
                _decisionPluginRegistry != null &&
                _decisionPluginRegistry.TryResolve(providerName, out var plugin))
            {
                var pluginResult = plugin.Evaluate(new PolicyDecisionPluginContext(
                    TenantId: instance.TenantId,
                    WorkflowInstanceId: instance.Id,
                    StepId: nextStep.StepId,
                    EventType: domainEvent.EventType,
                    Conditions: nextStep.Conditions,
                    Payload: context.Payload));

                if (pluginResult.IsMatched)
                {
                    decisionTarget = pluginResult.NextStepId;
                }
            }
            else
            {
                decisionTarget = EvaluateDecisionConditions(nextStep.Conditions, context.Payload);
            }

            if (decisionTarget != null)
            {
                instance.AdvanceTo(nextStepId);

                var targetStep = definition.Steps.FirstOrDefault(s => s.StepId == decisionTarget);
                if (targetStep == null)
                    return WorkflowAdvanceResult.Failed($"Decision target '{decisionTarget}' not found.");

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

    private string? EvaluateDecisionConditions(
        Dictionary<string, string> conditions,
        Dictionary<string, object>? payload)
    {
        foreach (var condition in conditions)
        {
            var expression = condition.Key;
            var target = condition.Value;

            try
            {
                if (payload != null && EvaluateCondition(expression, payload))
                {
                    return target;
                }
            }
            catch
            {
                // Skip malformed expressions gracefully.
            }
        }

        if (conditions.TryGetValue("Default", out var defaultTarget))
        {
            return defaultTarget;
        }

        return null;
    }
}
