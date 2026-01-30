using System.Collections.Generic;
using FlowOS.Domain.ValueObjects;

namespace FlowOS.StateMachines.Models;

public enum TransitionResultType
{
    Allowed,
    Denied,
    Ignored // Event not relevant to State Machine
}

public class TransitionResult
{
    public bool IsAllowed { get; }
    public TransitionResultType ResultType { get; }
    public string Reason { get; }
    public StateTransition? MatchedTransition { get; }

    private TransitionResult(bool isAllowed, TransitionResultType type, string reason, StateTransition? matchedTransition)
    {
        IsAllowed = isAllowed;
        ResultType = type;
        Reason = reason;
        MatchedTransition = matchedTransition;
    }

    public static TransitionResult Allowed(StateTransition transition) => 
        new(true, TransitionResultType.Allowed, "Transition allowed.", transition);

    public static TransitionResult Denied(string reason) => 
        new(false, TransitionResultType.Denied, reason, null);

    public static TransitionResult Ignored(string reason) =>
        new(true, TransitionResultType.Ignored, reason, null); // IsAllowed=true because it doesn't block
}
