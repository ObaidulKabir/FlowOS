using System;
using FlowOS.Workflows.Enums;

namespace FlowOS.Workflows.Domain;

public class WorkflowInstance : IWorkflowInstance
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    // Optional CorrelationId to link to external business entities or processes (e.g., OrderId, UserId)
    public Guid? CorrelationId { get; private set; }
    // Immutable properties captured at creation time
    public Guid WorkflowDefinitionId { get; private set; }
    public Guid WorkflowClassId { get; private set; } // Link to Governance Entity
    public int WorkflowVersion { get; private set; }
    public string CurrentStepId { get; private set; }
    public WorkflowInstanceStatus Status { get; private set; }

    // Orchestration state only - not business data

    protected WorkflowInstance()
    {
        CurrentStepId = null!;
    }

    public WorkflowInstance(Guid tenantId, Guid definitionId, Guid workflowClassId, int version, string initialStepId, Guid? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(initialStepId))
            throw new ArgumentNullException(nameof(initialStepId));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        WorkflowDefinitionId = definitionId;
        WorkflowClassId = workflowClassId;
        WorkflowVersion = version;
        CurrentStepId = initialStepId;
        Status = WorkflowInstanceStatus.Running;
        CorrelationId = correlationId;
        CreatedAt = DateTime.UtcNow;
    }

    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public void AdvanceTo(string nextStepId)
    {
        if (Status == WorkflowInstanceStatus.Completed || Status == WorkflowInstanceStatus.Failed)
            throw new InvalidOperationException("Cannot advance a terminated workflow.");

        CurrentStepId = nextStepId;
        Status = WorkflowInstanceStatus.Running;
    }

    public void Complete()
    {
        Status = WorkflowInstanceStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void Wait()
    {
        Status = WorkflowInstanceStatus.Waiting;
    }
}
