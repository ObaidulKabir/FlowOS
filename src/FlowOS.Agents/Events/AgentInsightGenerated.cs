using System;
using FlowOS.Events.Models;

namespace FlowOS.Agents.Events;

public class AgentInsightGenerated : DomainEvent
{
    public string AgentId { get; private set; }
    public string Insight { get; private set; }
    public string ContextObjective { get; private set; }

    // Extended telemetry — null-safe so existing callers remain unaffected
    /// <summary>The workflow instance this insight was generated for.</summary>
    public Guid? WorkflowInstanceId { get; private set; }
    /// <summary>The step ID at which the agent was executing.</summary>
    public string? StepId { get; private set; }
    /// <summary>The event type the agent suggested (before bounded-autonomy filtering).</summary>
    public string? SuggestedEvent { get; private set; }
    /// <summary>Agent-reported confidence in the suggestion (0–1).</summary>
    public double? Confidence { get; private set; }
    /// <summary>Provider name used for this execution (e.g. openai, anthropic, flowos-risk).</summary>
    public string? ProviderName { get; private set; }

    public AgentInsightGenerated(
        Guid tenantId,
        string agentId,
        string insight,
        string contextObjective,
        Guid? workflowInstanceId = null,
        string? stepId = null,
        string? suggestedEvent = null,
        double? confidence = null,
        string? providerName = null)
        : base(tenantId, "AgentInsightGenerated")
    {
        AgentId = agentId;
        Insight = insight;
        ContextObjective = contextObjective;
        WorkflowInstanceId = workflowInstanceId;
        StepId = stepId;
        SuggestedEvent = suggestedEvent;
        Confidence = confidence;
        ProviderName = providerName;
    }

    // For EF Core
    private AgentInsightGenerated()
    {
        AgentId = null!;
        Insight = null!;
        ContextObjective = null!;
    }
}
