using System;
using FlowOS.Domain;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Entities.Orchestration;

/// <summary>
/// Declarative multi-agent coordination plan template.
///
/// ADR-006: mirrors the WorkflowClass → WorkflowInstance pattern for agentic
/// workflows.  An AgentWorkflowClass stores an immutable AgentWorkflowBlueprint
/// (the "what") and produces AgentWorkflowExecution instances at runtime (the "how").
///
/// Lifecycle: Draft → Published → (Deprecated | Abandoned)
///   Draft       — editable, not dispatchable
///   Published   — immutable, dispatchable; new executions are created from this version
///   Deprecated  — no new executions; existing executions continue
///   Abandoned   — soft-deleted; no new executions, informational only
///
/// Versioning: PreviousVersionId forms a singly-linked lineage chain identical to
/// WorkflowClass.  When a published plan must change, create a new Draft from the
/// current version (carry PreviousVersionId forward), revise, and publish.
/// </summary>
public sealed class AgentWorkflowClass
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Human-readable plan name, e.g. "Quarterly Risk Assessment Plan".</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Semantic version string, e.g. "1.0.0".</summary>
    public string Version { get; private set; } = string.Empty;

    public WorkflowClassStatus Status { get; private set; }
    public WorkflowClassScope Scope { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? PublishedAt { get; private set; }

    // ── Lineage ───────────────────────────────────────────────────────────────

    /// <summary>Id of the previous published version (null for the first version).</summary>
    public Guid? PreviousVersionId { get; private set; }

    /// <summary>Human-readable summary of changes in this version.</summary>
    public string? ChangeLog { get; private set; }

    /// <summary>Why this version was deprecated and what to migrate to.</summary>
    public string? DeprecationReason { get; private set; }

    /// <summary>Target version id to migrate running executions to on deprecation.</summary>
    public Guid? DeprecationMigrationTargetId { get; private set; }

    /// <summary>FlowOS release this blueprint was authored against.</summary>
    public string FlowOsVersion { get; private set; } = FlowOsRelease.Version;

    // ── Blueprint (immutable after publish) ───────────────────────────────────

    /// <summary>
    /// The declarative plan definition.  Serialised as JSON (jsonb column).
    /// Immutable once Published — create a new version to change it.
    /// </summary>
    public AgentWorkflowBlueprint Definition { get; private set; } = null!;

    // ── EF Core constructor ────────────────────────────────────────────────────

    private AgentWorkflowClass() { }

    // ── Public constructor ─────────────────────────────────────────────────────

    public AgentWorkflowClass(
        Guid tenantId,
        string name,
        string version,
        AgentWorkflowBlueprint definition,
        DateTime createdAt,
        Guid? previousVersionId = null,
        string? changeLog = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Version is required.", nameof(version));
        if (definition is null) throw new ArgumentNullException(nameof(definition));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name;
        Version = version;
        Definition = definition;
        PreviousVersionId = previousVersionId;
        ChangeLog = changeLog;
        Status = WorkflowClassStatus.Draft;
        Scope = WorkflowClassScope.Private;
        FlowOsVersion = FlowOsRelease.Version;
        CreatedAt = createdAt.Kind == DateTimeKind.Utc ? createdAt : createdAt.ToUniversalTime();
    }

    // ── Lifecycle transitions ─────────────────────────────────────────────────

    /// <summary>Update a Draft before publishing.  Throws if already published.</summary>
    public void UpdateDraft(
        string name,
        string version,
        AgentWorkflowBlueprint definition,
        string? changeLog = null)
    {
        if (Status != WorkflowClassStatus.Draft)
            throw new InvalidOperationException("Only Draft plans can be updated.");

        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (definition is null) throw new ArgumentNullException(nameof(definition));

        Name = name;
        if (!string.IsNullOrWhiteSpace(version)) Version = version;
        Definition = definition;
        ChangeLog = changeLog;
        FlowOsVersion = FlowOsRelease.Version;
    }

    /// <summary>
    /// Publish — locks the blueprint and makes the plan dispatchable.
    /// </summary>
    public void Publish(DateTime publishedAt)
    {
        if (Status != WorkflowClassStatus.Draft)
            throw new InvalidOperationException("Only Draft plans can be published.");

        Status = WorkflowClassStatus.Published;
        PublishedAt = publishedAt;
    }

    /// <summary>Hard-delete guard — only Drafts with no executions may be deleted.</summary>
    public void Delete(bool hasExecutions)
    {
        if (Status != WorkflowClassStatus.Draft)
            throw new InvalidOperationException(
                "GOV-DEL-001: Only Draft plans can be hard-deleted. Use Abandon for published plans.");

        if (hasExecutions)
            throw new InvalidOperationException(
                "GOV-DEL-002: Cannot delete a plan that has existing executions.");
    }

    /// <summary>Deprecate — no new executions; existing ones continue.</summary>
    public void Deprecate(string? reason, Guid? migrationTargetId)
    {
        if (Status != WorkflowClassStatus.Published)
            throw new InvalidOperationException("Only Published plans can be deprecated.");

        Status = WorkflowClassStatus.Deprecated;
        DeprecationReason = reason;
        DeprecationMigrationTargetId = migrationTargetId;
    }

    /// <summary>Abandon — soft-delete a published or deprecated plan.</summary>
    public void Abandon()
    {
        if (Status == WorkflowClassStatus.Draft)
            throw new InvalidOperationException("Cannot abandon a Draft. Delete it instead.");

        if (Status == WorkflowClassStatus.Abandoned) return; // idempotent

        Status = WorkflowClassStatus.Abandoned;
    }
}
