using System.Collections.Generic;

namespace FlowOS.Agents.Abstractions;

public class AgentResult
{
    public bool Success { get; set; }
    public string? Insight { get; set; }
    public Dictionary<string, object> StructuredData { get; set; } = new();
    public string? FailureReason { get; set; }

    public static AgentResult FromInsight(string insight, Dictionary<string, object>? data = null)
    {
        return new AgentResult
        {
            Success = true,
            Insight = insight,
            StructuredData = data ?? new Dictionary<string, object>()
        };
    }

    public static AgentResult Failure(string reason)
    {
        return new AgentResult
        {
            Success = false,
            FailureReason = reason
        };
    }
}
