using System.Text.Json;
using FlowOS.Domain.ValueObjects;

namespace FlowOS.Domain.Entities;

public class WorkflowContextSnapshot
{
    public Guid WorkflowInstanceId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ContextBindingRevisionId { get; private set; }
    public Dictionary<string, JsonElement> CanonicalData { get; private set; }
    public string? SourceSystem { get; private set; }
    public string? ExternalEntityId { get; private set; }
    public Dictionary<string, string> BusinessMetadata { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    protected WorkflowContextSnapshot()
    {
        CanonicalData = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        BusinessMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public WorkflowContextSnapshot(
        Guid workflowInstanceId,
        Guid tenantId,
        Guid contextBindingRevisionId,
        IReadOnlyDictionary<string, JsonElement> canonicalData,
        WorkflowBusinessReference? businessReference = null)
    {
        if (workflowInstanceId == Guid.Empty) throw new ArgumentException("WorkflowInstanceId is required.", nameof(workflowInstanceId));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (contextBindingRevisionId == Guid.Empty) throw new ArgumentException("ContextBindingRevisionId is required.", nameof(contextBindingRevisionId));

        WorkflowInstanceId = workflowInstanceId;
        TenantId = tenantId;
        ContextBindingRevisionId = contextBindingRevisionId;
        CanonicalData = CloneData(canonicalData);
        SourceSystem = businessReference?.SourceSystem?.Trim();
        ExternalEntityId = businessReference?.ExternalEntityId?.Trim();
        BusinessMetadata = businessReference?.Metadata != null
            ? new Dictionary<string, string>(businessReference.Metadata, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ConcurrencyVersion = 1;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public void Merge(IReadOnlyDictionary<string, JsonElement> delta)
    {
        if (delta.Count == 0) return;

        foreach (var item in delta)
        {
            CanonicalData[item.Key] = item.Value.Clone();
        }

        ConcurrencyVersion++;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static Dictionary<string, JsonElement> CloneData(IReadOnlyDictionary<string, JsonElement> source)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in source)
        {
            result[item.Key] = item.Value.Clone();
        }
        return result;
    }
}
