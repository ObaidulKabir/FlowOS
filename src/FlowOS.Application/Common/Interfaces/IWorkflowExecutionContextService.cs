using System.Text.Json;
using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;
using FlowOS.Workflows.Domain;

namespace FlowOS.Application.Common.Interfaces;

public sealed record ActiveWorkflowContextBinding(
    WorkflowContextBinding Binding,
    WorkflowContextBindingRevision Revision,
    WorkflowDefinition WorkflowDefinition,
    WorkflowClass SourceWorkflowClass);

public sealed class PreparedWorkflowContext
{
    public WorkflowContextBindingRevision Revision { get; }
    public WorkflowContextSnapshot? Snapshot { get; }
    public Dictionary<string, JsonElement> Delta { get; }
    public Dictionary<string, object> Payload { get; }

    public PreparedWorkflowContext(
        WorkflowContextBindingRevision revision,
        WorkflowContextSnapshot? snapshot,
        Dictionary<string, JsonElement> delta,
        Dictionary<string, object> payload)
    {
        Revision = revision;
        Snapshot = snapshot;
        Delta = delta;
        Payload = payload;
    }

    public void CommitDelta()
    {
        Snapshot?.Merge(Delta);
    }
}

public interface IWorkflowExecutionContextService
{
    Task<ActiveWorkflowContextBinding> ResolveActiveAsync(
        Guid tenantId,
        Guid? contextBindingId,
        string? contextType,
        CancellationToken cancellationToken = default);

    PreparedWorkflowContext PrepareInitial(
        ActiveWorkflowContextBinding activeBinding,
        object? sourcePayload);

    Task<PreparedWorkflowContext?> PrepareForInstanceAsync(
        Guid tenantId,
        WorkflowDefinition definition,
        Guid workflowInstanceId,
        string? contextualEventType,
        object? sourcePayload,
        CancellationToken cancellationToken = default);

    Task<Dictionary<string, object>> PrepareSimulationAsync(
        Guid tenantId,
        WorkflowDefinition definition,
        IReadOnlyDictionary<string, object?> baseCanonicalContext,
        string? contextualEventType,
        object? sourcePayload,
        CancellationToken cancellationToken = default);

    Task<PreparedWorkflowContext?> PrepareCanonicalDeltaAsync(
        Guid tenantId,
        WorkflowDefinition definition,
        Guid workflowInstanceId,
        IReadOnlyDictionary<string, object> canonicalDelta,
        CancellationToken cancellationToken = default);

    WorkflowContextSnapshot CreateSnapshot(
        Guid workflowInstanceId,
        Guid tenantId,
        PreparedWorkflowContext prepared,
        WorkflowBusinessReference? businessReference);
}
