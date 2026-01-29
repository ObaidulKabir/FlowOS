using System;
using System.Collections.Generic;
using FlowOS.Events.Abstractions;

namespace FlowOS.Agents.Abstractions;

public class AgentContext
{
    public Guid TenantId { get; }
    public object EntitySnapshot { get; }
    public string WorkflowState { get; }
    public IEnumerable<IEvent> EventHistory { get; }
    public string Objective { get; }

    public AgentContext(
        Guid tenantId,
        object entitySnapshot,
        string workflowState,
        IEnumerable<IEvent> eventHistory,
        string objective)
    {
        TenantId = tenantId;
        EntitySnapshot = entitySnapshot;
        WorkflowState = workflowState;
        EventHistory = eventHistory ?? new List<IEvent>();
        Objective = objective;
    }
}
