using System;
using System.Linq;
using FlowOS.Domain;
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

    /// <summary>Human-readable summary of what changed in this version.</summary>
    public string? ChangeLog { get; private set; }

    /// <summary>Optional deprecation reason and migration guidance.</summary>
    public string? DeprecationReason { get; private set; }

    /// <summary>Optional target version to migrate to when this version is deprecated.</summary>
    public Guid? DeprecationMigrationTargetId { get; private set; }

    /// <summary>FlowOS release this blueprint was last authored against. Not the blueprint's own <see cref="Version"/>.</summary>
    public string FlowOsVersion { get; private set; } = FlowOsRelease.Version;

    /// <summary>
    /// The tenant application this workflow belongs to, or null when unassigned. Organizational metadata,
    /// not part of the blueprint, so it can change in any status. All versions of one workflow (same
    /// tenant and name) are kept in the same application, and a new version inherits it.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

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
        FlowOsVersion = FlowOsRelease.Version;
        CreatedAt = DateTime.UtcNow;
    }

    // Lifecycle Transitions (Strict)

    public void UpdateDraft(string name, string version, WorkflowClassBlueprint definition, string? changeLog = null)
    {
        if (Status != WorkflowClassStatus.Draft)
            throw new InvalidOperationException("Only Drafts can be updated.");

        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
        if (definition == null) throw new ArgumentNullException(nameof(definition));

        Name = name;
        if (!string.IsNullOrWhiteSpace(version)) Version = version;
        Definition = definition;
        ChangeLog = changeLog;
        FlowOsVersion = FlowOsRelease.Version;
    }

    /// <summary>Moves this version to an application, or unassigns it with null.</summary>
    public void AssignToApplication(Guid? applicationId)
    {
        ApplicationId = applicationId == Guid.Empty ? null : applicationId;
    }

    public void Delete(bool hasInstances)
    {
        if (Status != WorkflowClassStatus.Draft)
            // DX report F11: this used to say "Use Abandon", naming the internal domain method
            // rather than the MCP tool an agent can actually call. deprecate_workflowclass is the
            // directive an agent needs here.
            throw new InvalidOperationException("GOV-DEL-001: Only Drafts can be hard deleted. A Published WorkflowClass cannot be deleted; call deprecate_workflowclass instead.");
        
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

    public void SetDeprecation(string? reason, Guid? migrationTargetId)
    {
        DeprecationReason = reason;
        DeprecationMigrationTargetId = migrationTargetId;
    }
}
