using System;
using System.Collections.Generic;
using FlowOS.Workflows.Enums;

namespace FlowOS.Workflows.Domain;

public class WorkflowDefinition
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public int Version { get; private set; }
    public WorkflowStatus Status { get; private set; }
    public string StartStepId { get; private set; } = string.Empty;
    public Guid? SourceWorkflowClassId { get; private set; }
    public Guid? ContextBindingRevisionId { get; private set; }
    public Guid? StateMachineDefinitionId { get; private set; }
    
    public List<WorkflowStepDefinition> Steps { get; private set; }

    protected WorkflowDefinition() 
    {
        Name = null!;
        Steps = new List<WorkflowStepDefinition>();
    }

    public WorkflowDefinition(Guid tenantId, string name, int version = 1, string startStepId = "Start")
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name;
        Version = version;
        Status = WorkflowStatus.Draft;
        StartStepId = startStepId;
        Steps = new List<WorkflowStepDefinition>();
    }

    public void AddStep(WorkflowStepDefinition step)
    {
        if (Status != WorkflowStatus.Draft)
            throw new InvalidOperationException("Cannot modify workflow after publication.");
        
        Steps.Add(step);
    }

    public void SetContextLineage(
        Guid sourceWorkflowClassId,
        Guid contextBindingRevisionId,
        Guid stateMachineDefinitionId)
    {
        if (Status != WorkflowStatus.Draft)
            throw new InvalidOperationException("Cannot set context lineage after publication.");
        if (sourceWorkflowClassId == Guid.Empty) throw new ArgumentException("SourceWorkflowClassId is required.", nameof(sourceWorkflowClassId));
        if (contextBindingRevisionId == Guid.Empty) throw new ArgumentException("ContextBindingRevisionId is required.", nameof(contextBindingRevisionId));
        if (stateMachineDefinitionId == Guid.Empty) throw new ArgumentException("StateMachineDefinitionId is required.", nameof(stateMachineDefinitionId));

        SourceWorkflowClassId = sourceWorkflowClassId;
        ContextBindingRevisionId = contextBindingRevisionId;
        StateMachineDefinitionId = stateMachineDefinitionId;
    }

    public void Publish()
    {
        if (Steps.Count == 0)
            throw new InvalidOperationException("Cannot publish empty workflow.");
            
        Status = WorkflowStatus.Published;
    }

    public void Archive()
    {
        if (Status == WorkflowStatus.Archived) return;
        Status = WorkflowStatus.Archived;
    }
}
