using System;
using FlowOS.Workflows.Enums;

namespace FlowOS.Workflows.Domain;

public class WorkflowInstance
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public Guid WorkflowDefinitionId { get; private set; }
    public int WorkflowVersion { get; private set; }
    public string CurrentStepId { get; private set; }
    public WorkflowInstanceStatus Status { get; private set; }
    
    // Orchestration state only - not business data
    
    protected WorkflowInstance() 
    {
        CurrentStepId = null!;
    }

    public WorkflowInstance(Guid tenantId, Guid definitionId, int version, string initialStepId, Guid? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(initialStepId)) 
            throw new ArgumentNullException(nameof(initialStepId));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        WorkflowDefinitionId = definitionId;
        WorkflowVersion = version;
        CurrentStepId = initialStepId;
        Status = WorkflowInstanceStatus.Running;
        CorrelationId = correlationId;
    }

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
    }

    public void Wait()
    {
        Status = WorkflowInstanceStatus.Waiting;
    }
}
