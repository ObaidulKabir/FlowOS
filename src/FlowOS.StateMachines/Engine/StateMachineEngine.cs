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

        // 3. Constraint Validation (Placeholder for Part B)
        // This is where Role/Policy checks will hook in later.
        
        return TransitionResult.Allowed(transition);
    }
}
