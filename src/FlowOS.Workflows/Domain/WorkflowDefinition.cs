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
    
    public List<WorkflowStepDefinition> Steps { get; private set; }

    protected WorkflowDefinition() 
    {
        Name = null!;
        Steps = new List<WorkflowStepDefinition>();
    }

    public WorkflowDefinition(Guid tenantId, string name, int version = 1)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name;
        Version = version;
        Status = WorkflowStatus.Draft;
        Steps = new List<WorkflowStepDefinition>();
    }

    public void AddStep(WorkflowStepDefinition step)
    {
        if (Status != WorkflowStatus.Draft)
            throw new InvalidOperationException("Cannot modify workflow after publication.");
        
        Steps.Add(step);
    }

    public void Publish()
    {
        if (Steps.Count == 0)
            throw new InvalidOperationException("Cannot publish empty workflow.");
            
        Status = WorkflowStatus.Published;
    }
}
