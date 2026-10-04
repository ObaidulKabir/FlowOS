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
    public ContextSchemaDefinition? ContextSchema { get; }

    public PreparedWorkflowContext(
        WorkflowContextBindingRevision revision,
        WorkflowContextSnapshot? snapshot,
        Dictionary<string, JsonElement> delta,
        Dictionary<string, object> payload,
        ContextSchemaDefinition? contextSchema = null)
    {
        Revision = revision;
        Snapshot = snapshot;
        Delta = delta;
        Payload = payload;
        ContextSchema = contextSchema;
    }

    public void CommitDelta(string? actor = null)
    {
        Snapshot?.Merge(Delta, ContextSchema, actor, Recompute);
    }

    private void Recompute(Dictionary<string, JsonElement> data)
    {
        if (ContextSchema?.Computed == null || ContextSchema.Computed.Count == 0) return;

        var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in data)
        {
            payload[kvp.Key] = kvp.Value;
        }

        foreach (var (fieldName, compDef) in ContextSchema.Computed)
        {
            if (string.IsNullOrWhiteSpace(compDef.Expression)) continue;
            try
            {
                var val = FlowOS.StateMachines.Engine.ExpressionEvaluator.EvaluateValue(compDef.Expression, payload);
                if (val != null)
                {
                    var elem = JsonSerializer.SerializeToElement(val);
                    data[fieldName] = elem;
                    payload[fieldName] = val;
                }
            }
            catch { /* fail closed on evaluation error */ }
        }
    }
}

public sealed record PreparedWorkflowSimulationContext(
    WorkflowContextBindingRevision Revision,
    string? CanonicalEventType,
    Dictionary<string, JsonElement> Delta,
    Dictionary<string, JsonElement> CanonicalData,
    Dictionary<string, object> Payload,
    ContextSchemaDefinition? ContextSchema = null);

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

    PreparedWorkflowSimulationContext PrepareSimulationStep(
        WorkflowContextBindingRevision revision,
        WorkflowClass sourceWorkflowClass,
        IReadOnlyDictionary<string, object?> baseCanonicalContext,
        string? contextualEventType,
        object? sourcePayload,
        bool isInitial);

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
