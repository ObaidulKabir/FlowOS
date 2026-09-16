using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Events.Abstractions;

namespace FlowOS.Agents.Abstractions;

public class AgentContext
{
    public Guid TenantId { get; }
    public object EntitySnapshot { get; }
    public string WorkflowState { get; }
    public IEnumerable<IEvent> EventHistory { get; }
    public string Objective { get; }
    public DecisionPacket? Packet { get; }
    public IReadOnlyList<string> LegalEvents { get; }
    public bool RestrictToLegalEvents { get; }

    public AgentContext(
        Guid tenantId,
        object entitySnapshot,
        string workflowState,
        IEnumerable<IEvent> eventHistory,
        string objective,
        DecisionPacket? packet = null)
    {
        TenantId = tenantId;
        EntitySnapshot = entitySnapshot;
        WorkflowState = workflowState;
        EventHistory = eventHistory ?? new List<IEvent>();
        Objective = objective;
        Packet = packet;
        LegalEvents = packet?.LegalNextStepEvents ?? Array.Empty<string>();
        RestrictToLegalEvents = packet != null;
    }

    public static AgentContext FromPacket(DecisionPacket packet, IEnumerable<IEvent>? eventHistory = null)
    {
        var snapshot = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in packet.EventPayloads)
        {
            snapshot[item.Key] = item.Value;
        }

        foreach (var item in packet.CanonicalContext)
        {
            if (item.Value != null)
                snapshot[item.Key] = item.Value;
        }

        if (packet.ToolResults != null)
        {
            snapshot["ToolResults"] = packet.ToolResults;
        }

        return new AgentContext(
            packet.TenantId,
            snapshot,
            packet.CurrentState ?? packet.CurrentStepId,
            eventHistory ?? Array.Empty<IEvent>(),
            packet.Objective,
            packet);
    }
}
