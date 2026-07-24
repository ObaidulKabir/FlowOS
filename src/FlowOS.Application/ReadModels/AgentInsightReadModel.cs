using System;

namespace FlowOS.Application.ReadModels;

/// <summary>
/// Projection of <c>AgentInsightGenerated</c> events for task/UI queries.
/// Owned by the Application layer so handlers never depend on Infrastructure types.
/// </summary>
public class AgentInsightReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid WorkflowInstanceId { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string Insight { get; set; } = string.Empty;
    public string ContextObjective { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
