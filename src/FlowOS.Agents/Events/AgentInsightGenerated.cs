using System;
using FlowOS.Events.Models;

namespace FlowOS.Agents.Events;

public class AgentInsightGenerated : DomainEvent
{
    public string AgentId { get; private set; }
    public string Insight { get; private set; }
    public string ContextObjective { get; private set; }

    public AgentInsightGenerated(
        Guid tenantId,
        string agentId,
        string insight,
        string contextObjective)
        : base(tenantId, "AgentInsightGenerated")
    {
        AgentId = agentId;
        Insight = insight;
        ContextObjective = contextObjective;
    }

    // For EF Core
    private AgentInsightGenerated() { }
}
