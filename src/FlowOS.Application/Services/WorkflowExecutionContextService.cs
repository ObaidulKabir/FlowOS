using System.Text.Json;
using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.ValueObjects;
using FlowOS.Workflows.Enums;

namespace FlowOS.Application.Services;

public class WorkflowExecutionContextService : IWorkflowExecutionContextService
{
    private readonly IUnitOfWork _unitOfWork;

    public WorkflowExecutionContextService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ActiveWorkflowContextBinding> ResolveActiveAsync(
        Guid tenantId,
        Guid? contextBindingId,
        string? contextType,
        CancellationToken cancellationToken = default)
    {
        if (contextBindingId.HasValue == !string.IsNullOrWhiteSpace(contextType))
            throw new ArgumentException("Specify exactly one of ContextBindingId or ContextType.");

        var binding = contextBindingId.HasValue
            ? await _unitOfWork.WorkflowContextBindings.GetByIdAsNoTrackingAsync(
                contextBindingId.Value, tenantId, cancellationToken)
            : await _unitOfWork.WorkflowContextBindings.GetByContextTypeAsync(
                contextType!, tenantId, cancellationToken);

        if (binding == null ||
            binding.Status != WorkflowContextBindingStatus.Active ||
            !binding.ActiveRevisionId.HasValue)
        {
            throw new KeyNotFoundException("Active workflow context binding was not found.");
        }

        var revision = await _unitOfWork.WorkflowContextBindings
            .GetRevisionByIdAsNoTrackingAsync(binding.ActiveRevisionId.Value, cancellationToken);
        if (revision == null ||
            revision.BindingId != binding.Id ||
            revision.Status != WorkflowContextBindingRevisionStatus.Active ||
            !revision.WorkflowDefinitionId.HasValue)
        {
            throw new InvalidOperationException("The active context-binding revision is incomplete.");
        }

        var definition = await _unitOfWork.WorkflowDefinitions
            .GetByIdAsNoTrackingAsync(revision.WorkflowDefinitionId.Value, cancellationToken);
        if (definition == null ||
            definition.TenantId != tenantId ||
            definition.Status != WorkflowStatus.Published)
        {
            throw new InvalidOperationException("The active context binding has no published runtime definition.");
        }

        var source = await _unitOfWork.WorkflowClasses
            .GetByIdAsNoTrackingAsync(revision.SourceWorkflowClassId, cancellationToken)
            ?? throw new InvalidOperationException("The context binding source template no longer exists.");

        return new ActiveWorkflowContextBinding(binding, revision, definition, source);
    }

    public PreparedWorkflowContext PrepareInitial(
        ActiveWorkflowContextBinding activeBinding,
        object? sourcePayload)
    {
        var sourceRoot = ToObjectElement(sourcePayload);
        ValidatePayload(activeBinding.Revision.Definition.SourcePayloadSchema, sourceRoot, "source payload");

        var delta = Project(
            sourceRoot,
            activeBinding.Revision.Definition.InputMapping,
            activeBinding.Revision.Definition.ConditionParameters);

        ValidateCanonical(activeBinding.SourceWorkflowClass.Definition.ContextSchema, delta);
        return new PreparedWorkflowContext(
            activeBinding.Revision,
            null,
            delta,
            ToExecutionPayload(delta));
    }

    public async Task<PreparedWorkflowContext?> PrepareForInstanceAsync(
        Guid tenantId,
        FlowOS.Workflows.Domain.WorkflowDefinition definition,
        Guid workflowInstanceId,
        string? contextualEventType,
        object? sourcePayload,
        CancellationToken cancellationToken = default)
    {
        if (!definition.ContextBindingRevisionId.HasValue) return null;

        var revision = await _unitOfWork.WorkflowContextBindings
            .GetRevisionByIdAsNoTrackingAsync(definition.ContextBindingRevisionId.Value, cancellationToken);
        if (revision == null)
            throw new InvalidOperationException("Workflow context-binding revision was not found.");

        var binding = await _unitOfWork.WorkflowContextBindings
            .GetByIdAsNoTrackingAsync(revision.BindingId, tenantId, cancellationToken);
        if (binding == null)
            throw new KeyNotFoundException("Workflow context binding was not found.");

        var snapshot = await _unitOfWork.WorkflowContextSnapshots
            .GetAsync(workflowInstanceId, tenantId, cancellationToken);
        if (snapshot == null || snapshot.ContextBindingRevisionId != revision.Id)
            throw new InvalidOperationException("Workflow canonical context snapshot was not found.");

        var source = await _unitOfWork.WorkflowClasses
            .GetByIdAsNoTrackingAsync(revision.SourceWorkflowClassId, cancellationToken)
            ?? throw new InvalidOperationException("Workflow context source template was not found.");

        var canonicalEvent = ResolveCanonicalEvent(contextualEventType, revision.Definition.EventAliases);
        var mappings = new Dictionary<string, string>(
            revision.Definition.InputMapping,
            StringComparer.OrdinalIgnoreCase);
        if (canonicalEvent != null &&
            TryGetValue(revision.Definition.EventInputMappings, canonicalEvent, out var eventMappings))
        {
            foreach (var mapping in eventMappings)
            {
                mappings[mapping.Key] = mapping.Value;
            }
        }

        var delta = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (sourcePayload != null)
        {
            var sourceRoot = ToObjectElement(sourcePayload);
            var sourceSchema = canonicalEvent != null &&
                               TryGetValue(revision.Definition.EventSourcePayloadSchemas, canonicalEvent, out var eventSchema)
                ? eventSchema
                : revision.Definition.SourcePayloadSchema;
            ValidatePayload(sourceSchema, sourceRoot, "event source payload");
            delta = Project(
                sourceRoot,
                mappings,
                new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase));
        }
        var merged = Clone(snapshot.CanonicalData);
        foreach (var parameter in revision.Definition.ConditionParameters)
        {
            if (!merged.ContainsKey(parameter.Key))
            {
                merged[parameter.Key] = parameter.Value.Clone();
            }
        }
        foreach (var item in delta)
        {
            merged[item.Key] = item.Value.Clone();
        }

        ValidateCanonical(source.Definition.ContextSchema, merged);
        return new PreparedWorkflowContext(revision, snapshot, delta, ToExecutionPayload(merged));
    }

    public async Task<Dictionary<string, object>> PrepareSimulationAsync(
        Guid tenantId,
        FlowOS.Workflows.Domain.WorkflowDefinition definition,
        IReadOnlyDictionary<string, object?> baseCanonicalContext,
        string? contextualEventType,
        object? sourcePayload,
        CancellationToken cancellationToken = default)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in baseCanonicalContext)
        {
            merged[item.Key] = JsonSerializer.SerializeToElement(item.Value);
        }

        if (!definition.ContextBindingRevisionId.HasValue)
        {
            var sourceRoot = ToObjectElement(sourcePayload);
            foreach (var item in Project(
                         sourceRoot,
                         new Dictionary<string, string>(),
                         new Dictionary<string, JsonElement>(),
                         passThroughWhenUnmapped: true))
            {
                merged[item.Key] = item.Value;
            }
            return ToExecutionPayload(merged);
        }

        var revision = await _unitOfWork.WorkflowContextBindings
            .GetRevisionByIdAsNoTrackingAsync(definition.ContextBindingRevisionId.Value, cancellationToken)
            ?? throw new InvalidOperationException("Workflow context-binding revision was not found.");
        var source = await _unitOfWork.WorkflowClasses
            .GetByIdAsNoTrackingAsync(revision.SourceWorkflowClassId, cancellationToken)
            ?? throw new InvalidOperationException("Workflow context source template was not found.");

        var canonicalEvent = ResolveCanonicalEvent(contextualEventType, revision.Definition.EventAliases);
        var mappings = new Dictionary<string, string>(
            revision.Definition.InputMapping,
            StringComparer.OrdinalIgnoreCase);
        if (canonicalEvent != null &&
            TryGetValue(revision.Definition.EventInputMappings, canonicalEvent, out var eventMappings))
        {
            foreach (var mapping in eventMappings)
            {
                mappings[mapping.Key] = mapping.Value;
            }
        }

        foreach (var parameter in revision.Definition.ConditionParameters)
        {
            merged[parameter.Key] = parameter.Value.Clone();
        }
        if (sourcePayload != null)
        {
            var sourcePayloadRoot = ToObjectElement(sourcePayload);
            var sourceSchema = canonicalEvent != null &&
                               TryGetValue(revision.Definition.EventSourcePayloadSchemas, canonicalEvent, out var eventSchema)
                ? eventSchema
                : revision.Definition.SourcePayloadSchema;
            ValidatePayload(sourceSchema, sourcePayloadRoot, "simulation source payload");
            foreach (var item in Project(
                         sourcePayloadRoot,
                         mappings,
                         new Dictionary<string, JsonElement>()))
            {
                merged[item.Key] = item.Value;
            }
        }

        ValidateCanonical(source.Definition.ContextSchema, merged);
        return ToExecutionPayload(merged);
    }

    public async Task<PreparedWorkflowContext?> PrepareCanonicalDeltaAsync(
        Guid tenantId,
        FlowOS.Workflows.Domain.WorkflowDefinition definition,
        Guid workflowInstanceId,
        IReadOnlyDictionary<string, object> canonicalDelta,
        CancellationToken cancellationToken = default)
    {
        if (!definition.ContextBindingRevisionId.HasValue) return null;

        var revision = await _unitOfWork.WorkflowContextBindings
            .GetRevisionByIdAsNoTrackingAsync(definition.ContextBindingRevisionId.Value, cancellationToken)
            ?? throw new InvalidOperationException("Workflow context-binding revision was not found.");
        var snapshot = await _unitOfWork.WorkflowContextSnapshots
            .GetAsync(workflowInstanceId, tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Workflow canonical context snapshot was not found.");
        var source = await _unitOfWork.WorkflowClasses
            .GetByIdAsNoTrackingAsync(revision.SourceWorkflowClassId, cancellationToken)
            ?? throw new InvalidOperationException("Workflow context source template was not found.");

        var delta = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in canonicalDelta)
        {
            if (revision.Definition.ConditionParameters.Keys.Any(
                    key => string.Equals(key, item.Key, StringComparison.OrdinalIgnoreCase)))
            {
                throw new WorkflowContextPayloadException(
                    new[] { $"Canonical field '{item.Key}' is an immutable condition parameter." });
            }
            delta[item.Key] = JsonSerializer.SerializeToElement(item.Value);
        }

        var merged = Clone(snapshot.CanonicalData);
        foreach (var item in delta)
        {
            merged[item.Key] = item.Value;
        }
        ValidateCanonical(source.Definition.ContextSchema, merged);

        return new PreparedWorkflowContext(revision, snapshot, delta, ToExecutionPayload(merged));
    }

    public WorkflowContextSnapshot CreateSnapshot(
        Guid workflowInstanceId,
        Guid tenantId,
        PreparedWorkflowContext prepared,
        WorkflowBusinessReference? businessReference)
        => new(
            workflowInstanceId,
            tenantId,
            prepared.Revision.Id,
            prepared.Delta,
            businessReference);

    private static Dictionary<string, JsonElement> Project(
        JsonElement sourceRoot,
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyDictionary<string, JsonElement> parameters,
        bool passThroughWhenUnmapped = false)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        if (passThroughWhenUnmapped &&
            mappings.Count == 0 &&
            sourceRoot.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in sourceRoot.EnumerateObject())
            {
                result[property.Name] = property.Value.Clone();
            }
        }
        else
        {
            foreach (var mapping in mappings)
            {
                if (TryResolvePath(sourceRoot, mapping.Value, out var value))
                {
                    result[mapping.Key] = value.Clone();
                }
            }
        }

        foreach (var parameter in parameters)
        {
            result[parameter.Key] = parameter.Value.Clone();
        }

        return result;
    }

    private static JsonElement ToObjectElement(object? payload)
    {
        if (payload is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object) return element.Clone();
            throw new WorkflowContextPayloadException(new[] { "Payload must be a JSON object." });
        }

        var serialized = JsonSerializer.SerializeToElement(payload ?? new Dictionary<string, object>());
        if (serialized.ValueKind != JsonValueKind.Object)
            throw new WorkflowContextPayloadException(new[] { "Payload must be a JSON object." });
        return serialized;
    }

    private static bool TryResolvePath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }

            var matched = false;
            foreach (var property in value.EnumerateObject())
            {
                if (!string.Equals(property.Name, segment, StringComparison.OrdinalIgnoreCase)) continue;
                value = property.Value;
                matched = true;
                break;
            }
            if (!matched)
            {
                value = default;
                return false;
            }
        }
        return true;
    }

    private static Dictionary<string, object> ToExecutionPayload(
        IReadOnlyDictionary<string, JsonElement> values)
        => values.ToDictionary(
            item => item.Key,
            item => (object)item.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, JsonElement> Clone(
        IReadOnlyDictionary<string, JsonElement> values)
        => values.ToDictionary(
            item => item.Key,
            item => item.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);

    private static string? ResolveCanonicalEvent(
        string? contextualEvent,
        IReadOnlyDictionary<string, string> aliases)
    {
        if (string.IsNullOrWhiteSpace(contextualEvent)) return null;
        foreach (var alias in aliases)
        {
            if (string.Equals(alias.Value, contextualEvent, StringComparison.OrdinalIgnoreCase))
            {
                return alias.Key;
            }
        }
        return contextualEvent;
    }

    private static bool TryGetValue<T>(
        IReadOnlyDictionary<string, T> values,
        string key,
        out T value)
    {
        foreach (var item in values)
        {
            if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = item.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    private static void ValidateCanonical(
        string? schema,
        IReadOnlyDictionary<string, JsonElement> canonical)
    {
        if (string.IsNullOrWhiteSpace(schema)) return;
        var element = JsonSerializer.SerializeToElement(canonical);
        ValidatePayload(schema, element, "canonical context");
    }

    private static void ValidatePayload(string? schema, JsonElement payload, string label)
    {
        if (string.IsNullOrWhiteSpace(schema)) return;

        using var document = JsonDocument.Parse(schema);
        var errors = new List<string>();
        ValidateElement(document.RootElement, payload, "$", errors);
        if (errors.Count > 0)
        {
            throw new WorkflowContextPayloadException(
                errors.Select(error => $"{label}: {error}"));
        }
    }

    private static void ValidateElement(
        JsonElement schema,
        JsonElement value,
        string path,
        ICollection<string> errors)
    {
        if (schema.TryGetProperty("type", out var typeElement) &&
            typeElement.ValueKind == JsonValueKind.String &&
            !MatchesType(value, typeElement.GetString()))
        {
            errors.Add($"{path} must be of type '{typeElement.GetString()}'.");
            return;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required) &&
                required.ValueKind == JsonValueKind.Array)
            {
                foreach (var requiredName in required.EnumerateArray())
                {
                    if (requiredName.ValueKind != JsonValueKind.String) continue;
                    if (!TryGetProperty(value, requiredName.GetString()!, out _))
                    {
                        errors.Add($"{path}.{requiredName.GetString()} is required.");
                    }
                }
            }

            if (schema.TryGetProperty("properties", out var properties) &&
                properties.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertySchema in properties.EnumerateObject())
                {
                    if (TryGetProperty(value, propertySchema.Name, out var propertyValue))
                    {
                        ValidateElement(
                            propertySchema.Value,
                            propertyValue,
                            $"{path}.{propertySchema.Name}",
                            errors);
                    }
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array &&
                 schema.TryGetProperty("items", out var itemSchema))
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                ValidateElement(itemSchema, item, $"{path}[{index++}]", errors);
            }
        }
    }

    private static bool MatchesType(JsonElement value, string? expected)
        => expected?.ToLowerInvariant() switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true
        };

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.Ordinal)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }
}
