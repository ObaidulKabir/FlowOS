using System.Linq;
using FlowOS.Domain.Entities;
using FlowOS.Events.Abstractions;
using FlowOS.StateMachines.Models;

namespace FlowOS.StateMachines.Engine;

public class StateMachineEngine
{
    public TransitionResult ValidateTransition(
        StateMachineDefinition definition,
        string currentState,
        IEvent triggerEvent,
        FlowOS.StateMachines.Models.ExecutionContext context)
    {
        // 1. Basic State Validation
        if (!definition.States.Contains(currentState))
        {
            return TransitionResult.Denied($"Current state '{currentState}' is not valid for this definition.");
        }

        // 2. Find matching transitions (same event may be guarded by payload conditions)
        var candidates = definition.Transitions
            .Where(t =>
                t.FromState == currentState &&
                (t.EventId == triggerEvent.EventType || t.TriggerEventType == triggerEvent.EventType))
            .ToList();

        if (candidates.Count == 0)
        {
            var isKnownEvent = definition.Transitions.Any(t =>
                t.EventId == triggerEvent.EventType || t.TriggerEventType == triggerEvent.EventType);

            if (isKnownEvent)
            {
                return TransitionResult.Denied($"Event '{triggerEvent.EventType}' is not valid for current state '{currentState}'.");
            }

            return TransitionResult.Ignored($"Event '{triggerEvent.EventType}' is not defined in this State Machine.");
        }

        TransitionResult? lastDenied = null;
        foreach (var transition in candidates)
        {
            var denied = EvaluateConstraints(transition, context);
            if (denied == null)
                return TransitionResult.Allowed(transition);
            lastDenied = denied;
        }

        return lastDenied ?? TransitionResult.Denied($"Event '{triggerEvent.EventType}' is not valid for current state '{currentState}'.");
    }

    private static TransitionResult? EvaluateConstraints(
        FlowOS.Domain.ValueObjects.StateTransition transition,
        FlowOS.StateMachines.Models.ExecutionContext context)
    {
        if (transition.Constraints == null)
            return null;

        foreach (var constraint in transition.Constraints)
        {
            if (string.Equals(constraint.Key, "Expression", StringComparison.OrdinalIgnoreCase))
            {
                if (!ExpressionEvaluator.Evaluate(constraint.Value, context.Payload))
                {
                    return TransitionResult.Denied($"State Machine constraint violation: Expression '{constraint.Value}' evaluated to false.");
                }
            }
            else if (string.Equals(constraint.Key, "Role", StringComparison.OrdinalIgnoreCase))
            {
                if (!context.Metadata.TryGetValue("Roles", out var rolesObj) || rolesObj == null)
                {
                    return TransitionResult.Denied($"State Machine constraint violation: Role '{constraint.Value}' is required, but no roles were provided in context.");
                }

                var hasRole = false;
                if (rolesObj is IEnumerable<string> stringRoles)
                {
                    hasRole = stringRoles.Contains(constraint.Value);
                }
                else if (rolesObj is IEnumerable<object> objectRoles)
                {
                    hasRole = objectRoles.Any(r => r?.ToString() == constraint.Value);
                }
                else if (rolesObj is string roleString)
                {
                    hasRole = roleString.Split(',').Select(r => r.Trim()).Contains(constraint.Value);
                }

                if (!hasRole)
                {
                    return TransitionResult.Denied($"State Machine constraint violation: Required role '{constraint.Value}' is missing.");
                }
            }
        }

        return null;
    }
}
