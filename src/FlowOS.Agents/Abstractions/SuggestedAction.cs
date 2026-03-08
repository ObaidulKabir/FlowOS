using System.Collections.Generic;

namespace FlowOS.Agents.Abstractions;

public class SuggestedAction
{
    public string EventType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public Dictionary<string, object> Payload { get; set; } = new();

    public SuggestedAction() { }

    public SuggestedAction(string eventType, string reason, double confidence, Dictionary<string, object>? payload = null)
    {
        EventType = eventType;
        Reason = reason;
        Confidence = confidence;
        Payload = payload ?? new Dictionary<string, object>();
    }
}
