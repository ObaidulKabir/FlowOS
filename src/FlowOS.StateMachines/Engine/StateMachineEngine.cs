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
            t.TriggerEventType == triggerEvent.EventType);

        if (transition == null)
        {
            return TransitionResult.Denied($"No transition defined from '{currentState}' for event '{triggerEvent.EventType}'.");
        }

        // 3. Constraint Validation (Placeholder for Part B)
        // This is where Role/Policy checks will hook in later.
        
        return TransitionResult.Allowed(transition);
    }
}
