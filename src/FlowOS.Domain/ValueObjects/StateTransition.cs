using System.Collections.Generic;

namespace FlowOS.Domain.ValueObjects;

public class StateTransition
{
    public string FromState { get; set; } = string.Empty;
    public string ToState { get; set; } = string.Empty;
    public string TriggerEventType { get; set; } = string.Empty;
    public Dictionary<string, string> Constraints { get; set; } = new();

    public StateTransition() { }

    public StateTransition(string from, string to, string trigger)
    {
        FromState = from;
        ToState = to;
        TriggerEventType = trigger;
    }
}
