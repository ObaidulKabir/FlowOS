using System;

namespace FlowOS.Infrastructure.Persistence.ReadModels;

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
