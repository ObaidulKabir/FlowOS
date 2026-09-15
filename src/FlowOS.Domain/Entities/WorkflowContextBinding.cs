using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Entities;

public class WorkflowContextBinding
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string ContextType { get; private set; }
    public string NormalizedContextType { get; private set; }
    public string Name { get; private set; }
    public string NormalizedName { get; private set; }
    public WorkflowContextBindingStatus Status { get; private set; }
    public Guid? ActiveRevisionId { get; private set; }
    public Guid? DraftRevisionId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }

    protected WorkflowContextBinding()
    {
        ContextType = string.Empty;
        NormalizedContextType = string.Empty;
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    public WorkflowContextBinding(Guid tenantId, string contextType, string name)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(contextType)) throw new ArgumentException("ContextType is required.", nameof(contextType));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        ContextType = contextType.Trim();
        NormalizedContextType = Normalize(contextType);
        Name = name.Trim();
        NormalizedName = Normalize(name);
        Status = WorkflowContextBindingStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public void SetDraftRevision(Guid revisionId)
    {
        if (Status == WorkflowContextBindingStatus.Archived)
            throw new InvalidOperationException("Archived context bindings cannot be revised.");
        if (revisionId == Guid.Empty) throw new ArgumentException("RevisionId is required.", nameof(revisionId));

        DraftRevisionId = revisionId;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Activate(Guid revisionId)
    {
        if (Status == WorkflowContextBindingStatus.Archived)
            throw new InvalidOperationException("Archived context bindings cannot be activated.");
        if (revisionId == Guid.Empty) throw new ArgumentException("RevisionId is required.", nameof(revisionId));

        ActiveRevisionId = revisionId;
        if (DraftRevisionId == revisionId)
        {
            DraftRevisionId = null;
        }
        Status = WorkflowContextBindingStatus.Active;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Archive()
    {
        if (Status == WorkflowContextBindingStatus.Archived) return;

        Status = WorkflowContextBindingStatus.Archived;
        DraftRevisionId = null;
        ArchivedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = ArchivedAtUtc.Value;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
