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

        // 2. Find Matching Transition
        var transition = definition.Transitions.FirstOrDefault(t => 
            t.FromState == currentState && 
            (t.EventId == triggerEvent.EventType || t.TriggerEventType == triggerEvent.EventType)); // Dual check for compatibility

        if (transition == null)
        {
            // Check if this event is defined ANYWHERE in this State Machine
            var isKnownEvent = definition.Transitions.Any(t => 
                t.EventId == triggerEvent.EventType || t.TriggerEventType == triggerEvent.EventType);
            
            if (isKnownEvent)
            {
                return TransitionResult.Denied($"Event '{triggerEvent.EventType}' is not valid for current state '{currentState}'.");
            }
            else
            {
                // Event is unknown to this SM -> Ignore it (allow Workflow to handle it)
                return TransitionResult.Ignored($"Event '{triggerEvent.EventType}' is not defined in this State Machine.");
            }
        }

        // 3. Constraint Validation
        if (transition.Constraints != null)
        {
            foreach (var constraint in transition.Constraints)
            {
                if (constraint.Key == "Expression")
                {
                    if (!ExpressionEvaluator.Evaluate(constraint.Value, context.Payload))
                    {
                        return TransitionResult.Denied($"State Machine constraint violation: Expression '{constraint.Value}' evaluated to false.");
                    }
                }
                else if (constraint.Key == "Role")
                {
                    if (!context.Metadata.TryGetValue("Roles", out var rolesObj) || rolesObj == null)
                    {
                        return TransitionResult.Denied($"State Machine constraint violation: Role '{constraint.Value}' is required, but no roles were provided in context.");
                    }

                    // Support multiple data shapes for roles
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
                        // Handle comma-separated string or single role
                        hasRole = roleString.Split(',').Select(r => r.Trim()).Contains(constraint.Value);
                    }

                    if (!hasRole)
                    {
                        return TransitionResult.Denied($"State Machine constraint violation: Required role '{constraint.Value}' is missing.");
                    }
                }
            }
        }
        
        return TransitionResult.Allowed(transition);
    }
}
