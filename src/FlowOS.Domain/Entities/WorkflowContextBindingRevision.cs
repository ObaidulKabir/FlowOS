using FlowOS.Domain.Enums;
using FlowOS.Domain.ValueObjects;

namespace FlowOS.Domain.Entities;

public class WorkflowContextBindingRevision
{
    public Guid Id { get; private set; }
    public Guid BindingId { get; private set; }
    public int Revision { get; private set; }
    public Guid SourceWorkflowClassId { get; private set; }
    public string SourceWorkflowClassVersion { get; private set; }
    public WorkflowContextBindingDefinition Definition { get; private set; }
    public WorkflowContextBindingRevisionStatus Status { get; private set; }
    public Guid? WorkflowDefinitionId { get; private set; }
    public Guid? StateMachineDefinitionId { get; private set; }
    public string? ContentHash { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }
    public DateTime? SupersededAtUtc { get; private set; }

    protected WorkflowContextBindingRevision()
    {
        SourceWorkflowClassVersion = string.Empty;
        Definition = new WorkflowContextBindingDefinition();
    }

    public WorkflowContextBindingRevision(
        Guid bindingId,
        int revision,
        Guid sourceWorkflowClassId,
        string sourceWorkflowClassVersion,
        WorkflowContextBindingDefinition definition)
    {
        if (bindingId == Guid.Empty) throw new ArgumentException("BindingId is required.", nameof(bindingId));
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        if (sourceWorkflowClassId == Guid.Empty) throw new ArgumentException("SourceWorkflowClassId is required.", nameof(sourceWorkflowClassId));
        if (string.IsNullOrWhiteSpace(sourceWorkflowClassVersion)) throw new ArgumentException("Source workflow version is required.", nameof(sourceWorkflowClassVersion));

        Id = Guid.NewGuid();
        BindingId = bindingId;
        Revision = revision;
        SourceWorkflowClassId = sourceWorkflowClassId;
        SourceWorkflowClassVersion = sourceWorkflowClassVersion.Trim();
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Status = WorkflowContextBindingRevisionStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public void UpdateDraft(
        Guid sourceWorkflowClassId,
        string sourceWorkflowClassVersion,
        WorkflowContextBindingDefinition definition)
    {
        if (Status != WorkflowContextBindingRevisionStatus.Draft)
            throw new InvalidOperationException("Only draft context-binding revisions can be updated.");
        if (sourceWorkflowClassId == Guid.Empty) throw new ArgumentException("SourceWorkflowClassId is required.", nameof(sourceWorkflowClassId));
        if (string.IsNullOrWhiteSpace(sourceWorkflowClassVersion)) throw new ArgumentException("Source workflow version is required.", nameof(sourceWorkflowClassVersion));

        SourceWorkflowClassId = sourceWorkflowClassId;
        SourceWorkflowClassVersion = sourceWorkflowClassVersion.Trim();
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Activate(Guid workflowDefinitionId, Guid stateMachineDefinitionId, string contentHash)
    {
        if (Status != WorkflowContextBindingRevisionStatus.Draft)
            throw new InvalidOperationException("Only draft context-binding revisions can be activated.");
        if (workflowDefinitionId == Guid.Empty) throw new ArgumentException("WorkflowDefinitionId is required.", nameof(workflowDefinitionId));
        if (stateMachineDefinitionId == Guid.Empty) throw new ArgumentException("StateMachineDefinitionId is required.", nameof(stateMachineDefinitionId));
        if (string.IsNullOrWhiteSpace(contentHash)) throw new ArgumentException("ContentHash is required.", nameof(contentHash));

        WorkflowDefinitionId = workflowDefinitionId;
        StateMachineDefinitionId = stateMachineDefinitionId;
        ContentHash = contentHash;
        Status = WorkflowContextBindingRevisionStatus.Active;
        ActivatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = ActivatedAtUtc.Value;
    }

    public void Supersede()
    {
        if (Status == WorkflowContextBindingRevisionStatus.Superseded) return;
        if (Status != WorkflowContextBindingRevisionStatus.Active)
            throw new InvalidOperationException("Only active context-binding revisions can be superseded.");

        Status = WorkflowContextBindingRevisionStatus.Superseded;
        SupersededAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = SupersededAtUtc.Value;
    }
}
