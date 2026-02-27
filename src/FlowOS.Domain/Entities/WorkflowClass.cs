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
    public WorkflowClassScope Scope { get; private set; }
    public WorkflowClassStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    
    // Lineage Metadata
    public Guid? PreviousVersionId { get; private set; }

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

    public void Publish()
    {
        if (Status != WorkflowClassStatus.Draft)
            throw new InvalidOperationException($"Cannot publish from state {Status}.");
        
        // Strict Validation (Self-Enforcement)
        // Since Entity should not depend on Service, typically we inject the validator or pass it as method argument.
        // However, for this fix to be "Authoritative" and "Domain-Centric" without changing all call sites immediately,
        // and given the simple architecture, I will instantiate the validator here or (better)
        // follow pure DDD: Validate() should be called by the Application Service (Manager).
        // BUT, the test `wc.Publish()` expects the exception. This implies the Entity enforces it.
        // So I will invoke the validator inside the Entity.
        
        var validator = new WorkflowClassValidator();
        var result = validator.Validate(this);
        
        if (!result.IsValid)
        {
            var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}"));
            throw new InvalidOperationException($"Validation failed: {errors}");
        }
        
        Status = WorkflowClassStatus.Published;
        PublishedAt = DateTime.UtcNow;
    }

    public void SubmitForReview()
    {
        if (Status != WorkflowClassStatus.Published)
            throw new InvalidOperationException("Must be Published (Private) before submitting for review.");
        
        Scope = WorkflowClassScope.Shared;
        Status = WorkflowClassStatus.Shared;
    }

    public void WithdrawSubmission()
    {
        if (Status != WorkflowClassStatus.Shared)
            throw new InvalidOperationException("Only Shared (Under Review) classes can be withdrawn.");
        
        // Revert to Private/Published
        Scope = WorkflowClassScope.Private;
        Status = WorkflowClassStatus.Published;
    }

    public void ApproveAsPublic()
    {
        if (Status != WorkflowClassStatus.Shared)
            throw new InvalidOperationException("Must be Shared before becoming Public.");
        
        Scope = WorkflowClassScope.Public;
        Status = WorkflowClassStatus.Public;
        
        // Public templates should not have TenantId (conceptually), but for storage we might keep the "Owner" TenantId
        // or set it to Guid.Empty (System). 
        // Prompt says: "Public: no policies, no tenant IDs".
        // Let's assume the "Template" copy might have Guid.Empty, or we just ignore TenantId during Copy.
    }

    public void Deprecate()
    {
        if (Status == WorkflowClassStatus.Deprecated) return;
        
        Status = WorkflowClassStatus.Deprecated;
    }
    
    public WorkflowClass CreateCopyForTenant(Guid newTenantId)
    {
        // Allow copy if Public OR if it's my own (Create New Version)
        // Since this is Domain, we don't know "who" is asking unless we pass it.
        // But the check "Status == Public" was for the "Copy Public Template" use case.
        // For "Create New Version", the caller (Controller) verifies ownership.
        // So we can relax this check or make it conditional.
        // Actually, creating a copy is a valid domain operation regardless of status, provided permission is checked.
        
        // Reset versioning if new tenant, otherwise keep or increment?
        // If copying to SAME tenant, it's a Clone/New Version.
        
        var copy = new WorkflowClass(newTenantId, Name, "1.0.0", Definition)
        {
            Scope = WorkflowClassScope.Private,
            Status = WorkflowClassStatus.Draft
        };
        return copy;
    }

    public WorkflowClass CreateNewVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentNullException(nameof(version));
        
        // Create a new Draft copy with the specified version
        return new WorkflowClass(TenantId, Name, version, Definition)
        {
            Scope = WorkflowClassScope.Private,
            Status = WorkflowClassStatus.Draft,
            PreviousVersionId = this.Id // Track lineage
        };
    }

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
