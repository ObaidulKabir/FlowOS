using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FlowOS.Domain.ValueObjects;

namespace FlowOS.Domain.Entities;

public class WorkflowContextSnapshot
{
    private Dictionary<string, JsonElement>? _systemData;
    private Dictionary<string, List<string>>? _resolvedRoles;
    private Dictionary<string, string>? _correlationKeys;

    public Guid WorkflowInstanceId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ContextBindingRevisionId { get; private set; }

    // §1 Identity
    public string? EntityType
    {
        get => BusinessMetadata.TryGetValue("_entityType", out var val) ? val : null;
        private set
        {
            if (value != null) BusinessMetadata["_entityType"] = value;
            else BusinessMetadata.Remove("_entityType");
        }
    }

    public string? EntityId
    {
        get => BusinessMetadata.TryGetValue("_entityId", out var val) ? val : null;
        private set
        {
            if (value != null) BusinessMetadata["_entityId"] = value;
            else BusinessMetadata.Remove("_entityId");
        }
    }

    public Dictionary<string, string> CorrelationKeys
    {
        get
        {
            if (_correlationKeys == null)
            {
                _correlationKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (BusinessMetadata.TryGetValue("_correlation", out var json) && !string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (parsed != null)
                        {
                            foreach (var kvp in parsed) _correlationKeys[kvp.Key] = kvp.Value;
                        }
                    }
                    catch { /* fallback to empty */ }
                }
            }
            return _correlationKeys;
        }
    }

    public string? SourceSystem { get; private set; }
    public string? ExternalEntityId { get; private set; }
    public Dictionary<string, string> BusinessMetadata { get; private set; }

    // §2 Business Data (Canonical Fields)
    public Dictionary<string, JsonElement> CanonicalData { get; private set; }

    // §3 System Metadata (_auditTrail, _stepTimestamps, etc.)
    public Dictionary<string, JsonElement> SystemData
    {
        get
        {
            if (_systemData == null)
            {
                _systemData = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                if (BusinessMetadata.TryGetValue("_system", out var json) && !string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                        if (parsed != null)
                        {
                            foreach (var kvp in parsed) _systemData[kvp.Key] = kvp.Value.Clone();
                        }
                    }
                    catch { /* fallback to empty */ }
                }
            }
            return _systemData;
        }
    }

    // §4 Resolved Role Cache
    public Dictionary<string, List<string>> ResolvedRoles
    {
        get
        {
            if (_resolvedRoles == null)
            {
                _resolvedRoles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                if (BusinessMetadata.TryGetValue("_roles", out var json) && !string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                        if (parsed != null)
                        {
                            foreach (var kvp in parsed) _resolvedRoles[kvp.Key] = kvp.Value;
                        }
                    }
                    catch { /* fallback to empty */ }
                }
            }
            return _resolvedRoles;
        }
    }

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
        WorkflowBusinessReference? businessReference = null,
        ContextSchemaDefinition? schema = null)
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

        if (schema != null)
        {
            EntityType = schema.EntityType;
            if (!string.IsNullOrEmpty(schema.EntityIdField) && CanonicalData.TryGetValue(schema.EntityIdField, out var idElem))
            {
                EntityId = idElem.ToString();
            }

            foreach (var corrField in schema.CorrelationFields)
            {
                if (CanonicalData.TryGetValue(corrField, out var corrVal))
                {
                    CorrelationKeys[corrField] = corrVal.ToString() ?? string.Empty;
                }
            }
            SyncCorrelationMetadata();
        }

        ConcurrencyVersion = 1;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;

        RecordAuditEntry("Initialized", new List<ContextOperation>());
    }

    public void SetResolvedRoles(IReadOnlyDictionary<string, List<string>> roles)
    {
        _resolvedRoles = new Dictionary<string, List<string>>(roles, StringComparer.OrdinalIgnoreCase);
        BusinessMetadata["_roles"] = JsonSerializer.Serialize(_resolvedRoles);
    }

    public void ApplyOperations(
        IReadOnlyList<ContextOperation> operations,
        ContextSchemaDefinition? schema = null,
        string? actor = null,
        Action<Dictionary<string, JsonElement>>? recomputer = null)
    {
        if (operations == null || operations.Count == 0) return;

        foreach (var op in operations)
        {
            if (string.IsNullOrWhiteSpace(op.Field)) continue;

            // Enforce immutability
            if (schema != null && schema.Fields.TryGetValue(op.Field, out var fieldDef) && fieldDef.Immutable)
            {
                if (CanonicalData.TryGetValue(op.Field, out var existing))
                {
                    var existingText = existing.GetRawText();
                    var opText = op.Value.GetRawText();
                    if (!string.Equals(existingText, opText, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Context field '{op.Field}' is immutable and cannot be updated once set.");
                    }
                }
            }

            switch (op.Type)
            {
                case ContextOperationType.Set:
                    CanonicalData[op.Field] = op.Value.Clone();
                    break;

                case ContextOperationType.Increment:
                    decimal currentVal = 0;
                    if (CanonicalData.TryGetValue(op.Field, out var curElem) && curElem.ValueKind == JsonValueKind.Number)
                    {
                        currentVal = curElem.GetDecimal();
                    }
                    decimal deltaVal = 1;
                    if (op.Value.ValueKind == JsonValueKind.Number)
                    {
                        deltaVal = op.Value.GetDecimal();
                    }
                    CanonicalData[op.Field] = JsonSerializer.SerializeToElement(currentVal + deltaVal);
                    break;

                case ContextOperationType.Append:
                    var items = new List<JsonElement>();
                    if (CanonicalData.TryGetValue(op.Field, out var curArr) && curArr.ValueKind == JsonValueKind.Array)
                    {
                        items.AddRange(curArr.EnumerateArray().Select(x => x.Clone()));
                    }

                    if (op.Value.ValueKind == JsonValueKind.Array)
                    {
                        items.AddRange(op.Value.EnumerateArray().Select(x => x.Clone()));
                    }
                    else
                    {
                        items.Add(op.Value.Clone());
                    }
                    CanonicalData[op.Field] = JsonSerializer.SerializeToElement(items);
                    break;

                case ContextOperationType.DeepMerge:
                    if (CanonicalData.TryGetValue(op.Field, out var curObj) &&
                        curObj.ValueKind == JsonValueKind.Object &&
                        op.Value.ValueKind == JsonValueKind.Object)
                    {
                        CanonicalData[op.Field] = MergeObjects(curObj, op.Value);
                    }
                    else
                    {
                        CanonicalData[op.Field] = op.Value.Clone();
                    }
                    break;

                case ContextOperationType.SetIfAbsent:
                    if (!CanonicalData.ContainsKey(op.Field) || CanonicalData[op.Field].ValueKind == JsonValueKind.Null)
                    {
                        CanonicalData[op.Field] = op.Value.Clone();
                    }
                    break;
            }

            // Sync Identity fields
            if (schema != null)
            {
                if (string.Equals(op.Field, schema.EntityIdField, StringComparison.OrdinalIgnoreCase))
                {
                    EntityId = op.Value.ToString();
                }

                if (schema.CorrelationFields.Contains(op.Field, StringComparer.OrdinalIgnoreCase))
                {
                    CorrelationKeys[op.Field] = op.Value.ToString() ?? string.Empty;
                    SyncCorrelationMetadata();
                }
            }
        }

        // Trigger reactive recomputation
        recomputer?.Invoke(CanonicalData);

        ConcurrencyVersion++;
        UpdatedAtUtc = DateTime.UtcNow;

        RecordAuditEntry(actor ?? "System", operations);
    }

    public void Merge(
        IReadOnlyDictionary<string, JsonElement> delta,
        ContextSchemaDefinition? schema = null,
        string? actor = null,
        Action<Dictionary<string, JsonElement>>? recomputer = null)
    {
        if (delta == null || delta.Count == 0) return;

        var operations = new List<ContextOperation>();
        foreach (var item in delta)
        {
            // If field is configured as append-only in schema, map to Append operation
            if (schema != null && schema.Fields.TryGetValue(item.Key, out var fieldDef) && fieldDef.AppendOnly)
            {
                operations.Add(ContextOperation.Append(item.Key, item.Value));
            }
            // If field is a counter, check if delta is numeric increment or default to set
            else if (schema != null && schema.Fields.TryGetValue(item.Key, out fieldDef) &&
                     string.Equals(fieldDef.Type, "counter", StringComparison.OrdinalIgnoreCase) &&
                     item.Value.ValueKind == JsonValueKind.Number)
            {
                operations.Add(ContextOperation.Increment(item.Key, item.Value.GetDecimal()));
            }
            else
            {
                operations.Add(ContextOperation.Set(item.Key, item.Value));
            }
        }

        ApplyOperations(operations, schema, actor, recomputer);
    }

    private void SyncCorrelationMetadata()
    {
        if (_correlationKeys != null && _correlationKeys.Count > 0)
        {
            BusinessMetadata["_correlation"] = JsonSerializer.Serialize(_correlationKeys);
        }
    }

    private void RecordAuditEntry(string actor, IReadOnlyList<ContextOperation> operations)
    {
        var auditList = new List<object>();
        if (SystemData.TryGetValue("_auditTrail", out var curTrail) && curTrail.ValueKind == JsonValueKind.Array)
        {
            try
            {
                var existing = JsonSerializer.Deserialize<List<object>>(curTrail.GetRawText());
                if (existing != null) auditList.AddRange(existing);
            }
            catch { /* fallback to new list */ }
        }

        var entry = new Dictionary<string, object>
        {
            ["at"] = DateTime.UtcNow.ToString("O"),
            ["version"] = ConcurrencyVersion,
            ["actor"] = actor,
            ["mutations"] = operations.Select(o => $"{o.Type}:{o.Field}").ToList()
        };
        auditList.Add(entry);

        // Keep sliding window of latest 50 audit entries in context to bound document size
        if (auditList.Count > 50)
        {
            auditList = auditList.Skip(auditList.Count - 50).ToList();
        }

        SystemData["_auditTrail"] = JsonSerializer.SerializeToElement(auditList);
        SystemData["_lastModifiedAt"] = JsonSerializer.SerializeToElement(DateTime.UtcNow.ToString("O"));
        SystemData["_lastModifiedBy"] = JsonSerializer.SerializeToElement(actor);

        // Persist to BusinessMetadata
        BusinessMetadata["_system"] = JsonSerializer.Serialize(SystemData);
    }

    private static JsonElement MergeObjects(JsonElement target, JsonElement source)
    {
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in target.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.Clone();
        }
        foreach (var prop in source.EnumerateObject())
        {
            if (dict.TryGetValue(prop.Name, out var existing) &&
                existing.ValueKind == JsonValueKind.Object &&
                prop.Value.ValueKind == JsonValueKind.Object)
            {
                dict[prop.Name] = MergeObjects(existing, prop.Value);
            }
            else
            {
                dict[prop.Name] = prop.Value.Clone();
            }
        }
        return JsonSerializer.SerializeToElement(dict);
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
