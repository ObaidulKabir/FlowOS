using System.Collections.Generic;
using FlowOS.Domain.ValueObjects;

namespace FlowOS.StateMachines.Models;

public class TransitionResult
{
    public bool IsAllowed { get; }
    public string Reason { get; }
    public StateTransition? MatchedTransition { get; }

    private TransitionResult(bool isAllowed, string reason, StateTransition? matchedTransition)
    {
        IsAllowed = isAllowed;
        Reason = reason;
        MatchedTransition = matchedTransition;
    }

    public static TransitionResult Allowed(StateTransition transition) => 
        new(true, "Transition allowed.", transition);

    public static TransitionResult Denied(string reason) => 
        new(false, reason, null);
}
