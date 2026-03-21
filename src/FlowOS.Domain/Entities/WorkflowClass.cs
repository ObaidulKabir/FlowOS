using System;
using System.Linq;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;

namespace FlowOS.Domain.Entities;

public class WorkflowClass
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string Version { get; private set; } // e.g. "1.0.0"
    public WorkflowClassScope Scope { get; internal set; }
    public WorkflowClassStatus Status { get; internal set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? PublishedAt { get; internal set; }
    
    // Lineage Metadata
    public Guid? PreviousVersionId { get; internal set; }

    // The Configuration Pack (Immutable after publish)
    public WorkflowClassBlueprint Definition { get; private set; }

    protected WorkflowClass() 
    { 
        Name = null!;
        Version = null!;
        Definition = null!;
    }

    public WorkflowClass(Guid tenantId, string name, string version, WorkflowClassBlueprint definition)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentNullException(nameof(version));
        if (definition == null) throw new ArgumentNullException(nameof(definition));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name;
        Version = version;
        Scope = WorkflowClassScope.Private;
        Status = WorkflowClassStatus.Draft;
        Definition = definition;
        CreatedAt = DateTime.UtcNow;
    }

    // Lifecycle Transitions (Strict)

    public void UpdateDraft(string name, string version, WorkflowClassBlueprint definition)
    {
        if (Status != WorkflowClassStatus.Draft)
            throw new InvalidOperationException("Only Drafts can be updated.");

        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
        if (definition == null) throw new ArgumentNullException(nameof(definition));

        Name = name;
        if (!string.IsNullOrWhiteSpace(version)) Version = version;
        Definition = definition;
    }

    public void Delete(bool hasInstances)
    {
        if (Status != WorkflowClassStatus.Draft)
            throw new InvalidOperationException("GOV-DEL-001: Only Drafts can be hard deleted. Use Abandon for published workflows.");
        
        if (hasInstances)
            throw new InvalidOperationException("GOV-DEL-002: Cannot delete a workflow class that has existing instances.");
            
        // If checks pass, the repository will perform the deletion.
        // Domain object doesn't delete itself from DB, but it validates the rule.
    }

    public void Abandon(Guid requesterTenantId)
    {
        if (Status == WorkflowClassStatus.Draft)
            throw new InvalidOperationException("Cannot abandon a Draft. Delete it instead.");
            
        if (Status == WorkflowClassStatus.Public && requesterTenantId != Guid.Empty) // Assuming Guid.Empty is Admin
            throw new InvalidOperationException("GOV-ABN-001: Public workflow templates cannot be abandoned by tenants.");

        if (Status == WorkflowClassStatus.Abandoned)
            return; // Idempotent

        Status = WorkflowClassStatus.Abandoned;
    }
}
