using System.Text.Json;
using System.Text.RegularExpressions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Validation;
using FlowOS.Domain.ValueObjects;

namespace FlowOS.Application.Services;

public class WorkflowContextBindingValidator : IWorkflowContextBindingValidator
{
    private static readonly Regex IdentifierPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SourcePathPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ReservedCanonicalNames = new(
        ["it", "parent", "root", "new", "null", "true", "false"],
        StringComparer.OrdinalIgnoreCase);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPolicyDecisionPluginRegistry _decisionPluginRegistry;

    public WorkflowContextBindingValidator(
        IUnitOfWork unitOfWork,
        IPolicyDecisionPluginRegistry decisionPluginRegistry)
    {
        _unitOfWork = unitOfWork;
        _decisionPluginRegistry = decisionPluginRegistry;
    }

    public Task<ValidationResult> ValidateAsync(
        WorkflowContextBinding binding,
        WorkflowContextBindingRevision revision,
        CancellationToken cancellationToken = default)
        => ValidateAsync(binding, revision, WorkflowContextBindingValidationOptions.Strict, cancellationToken);

    public async Task<ValidationResult> ValidateAsync(
        WorkflowContextBinding binding,
        WorkflowContextBindingRevision revision,
        WorkflowContextBindingValidationOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();
        var source = await _unitOfWork.WorkflowClasses
            .GetByIdAsNoTrackingAsync(revision.SourceWorkflowClassId, cancellationToken);

        if (source == null ||
            (source.TenantId != binding.TenantId && source.Scope != WorkflowClassScope.Public))
        {
            result.AddError("CTX-SOURCE-001", "Source", "Source workflow template was not found.", "SourceWorkflowClassId");
            return result;
        }

        if (options.RequirePublishedSource &&
            source.Status != WorkflowClassStatus.Published &&
            source.Status != WorkflowClassStatus.Public)
        {
            result.AddError(
                "CTX-SOURCE-002",
                "Source",
                $"Source workflow template must be Published or Public before activation, but is {source.Status}. Draft sources are allowed for draft bindings and simulate_context_binding.",
                "SourceWorkflowClassId");
        }

        if (string.IsNullOrWhiteSpace(revision.Definition.EntityType))
        {
            result.AddError("CTX-ENTITY-001", "Mapping", "EntityType is required.", "Definition.EntityType");
        }

        if (!ValidateDefinitionCollections(revision.Definition, result))
        {
            return result;
        }

        ValidateEventAliases(source, revision, result);
        ValidateRoles(source, revision, result);
        ValidateCapabilities(source, revision, result);
        await ValidateRoleCapabilityGrantsAsync(
            source,
            binding.TenantId,
            revision,
            result,
            options.RequireExistingTenantRoles,
            cancellationToken);
        ValidateMappings(source, revision, result);
        ValidateDecisionProviders(source, revision, result);
        ValidateSchemas(source, revision, result);

        return result;
    }

    private static bool ValidateDefinitionCollections(
        WorkflowContextBindingDefinition definition,
        ValidationResult result)
    {
        var collections = new (string Name, object? Value)[]
        {
            ("EventAliases", definition.EventAliases),
            ("RoleOverrides", definition.RoleOverrides),
            ("CapabilityOverrides", definition.CapabilityOverrides),
            ("InputMapping", definition.InputMapping),
            ("EventInputMappings", definition.EventInputMappings),
            ("ConditionParameters", definition.ConditionParameters),
            ("DecisionProviderOverrides", definition.DecisionProviderOverrides),
            ("EventSourcePayloadSchemas", definition.EventSourcePayloadSchemas),
            ("Metadata", definition.Metadata)
        };

        var valid = true;
        foreach (var collection in collections)
        {
            if (collection.Value != null) continue;
            valid = false;
            result.AddError(
                "CTX-DEF-001",
                "Mapping",
                $"{collection.Name} cannot be null.",
                $"Definition.{collection.Name}");
        }

        if (definition.EventInputMappings != null)
        {
            foreach (var eventMapping in definition.EventInputMappings)
            {
                if (eventMapping.Value != null) continue;
                valid = false;
                result.AddError(
                    "CTX-DEF-001",
                    "Mapping",
                    $"Event input mapping '{eventMapping.Key}' cannot be null.",
                    $"Definition.EventInputMappings.{eventMapping.Key}");
            }
        }

        return valid;
    }

    private static void ValidateEventAliases(
        WorkflowClass source,
        WorkflowContextBindingRevision revision,
        ValidationResult result)
    {
        var declared = source.Definition.Events
            .Select(x => x.EventId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var effective = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in revision.Definition.EventAliases)
        {
            if (!declared.Contains(alias.Key))
            {
                result.AddError(
                    "CTX-EVT-001",
                    "Events",
                    $"Event alias references undeclared template event '{alias.Key}'.",
                    $"Definition.EventAliases.{alias.Key}");
            }

            if (string.IsNullOrWhiteSpace(alias.Value))
            {
                result.AddError(
                    "CTX-EVT-002",
                    "Events",
                    $"Contextual event ID for '{alias.Key}' is required.",
                    $"Definition.EventAliases.{alias.Key}");
            }
        }

        foreach (var eventId in declared)
        {
            var mapped = revision.Definition.EventAliases.TryGetValue(eventId, out var alias)
                ? alias.Trim()
                : eventId;
            if (!effective.Add(mapped))
            {
                result.AddError(
                    "CTX-EVT-003",
                    "Events",
                    $"Event aliases produce duplicate contextual event ID '{mapped}'.",
                    "Definition.EventAliases");
            }
        }
    }

    private static void ValidateRoles(
        WorkflowClass source,
        WorkflowContextBindingRevision revision,
        ValidationResult result)
    {
        // Business-context roles (source.Definition.Roles) belong to the modeled application, not
        // to FlowOS's own tenant Role/TenantUserRole tables, so a RoleOverride is just a rename
        // within that business vocabulary — there is no FlowOS Role row for it to match against.
        var declared = source.Definition.Roles
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var role in revision.Definition.RoleOverrides)
        {
            if (!declared.Contains(role.Key))
            {
                result.AddError(
                    "CTX-ROLE-001",
                    "Roles",
                    $"Role override references undeclared template role '{role.Key}'.",
                    $"Definition.RoleOverrides.{role.Key}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(role.Value))
            {
                result.AddError(
                    "CTX-ROLE-002",
                    "Roles",
                    $"Role override for '{role.Key}' maps to an empty name.",
                    $"Definition.RoleOverrides.{role.Key}");
            }
        }
    }

    private static void ValidateCapabilities(
        WorkflowClass source,
        WorkflowContextBindingRevision revision,
        ValidationResult result)
    {
        var declared = source.Definition.Capabilities
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var capability in revision.Definition.CapabilityOverrides)
        {
            if (!declared.Contains(capability.Key) &&
                !capability.Key.StartsWith("event.publish.", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(
                    "CTX-CAP-001",
                    "Capabilities",
                    $"Capability override references undeclared template capability '{capability.Key}'.",
                    $"Definition.CapabilityOverrides.{capability.Key}");
            }
            if (string.IsNullOrWhiteSpace(capability.Value))
            {
                result.AddError(
                    "CTX-CAP-002",
                    "Capabilities",
                    $"Mapped capability for '{capability.Key}' is required.",
                    $"Definition.CapabilityOverrides.{capability.Key}");
            }
        }
    }

    private async Task ValidateRoleCapabilityGrantsAsync(
        WorkflowClass source,
        Guid tenantId,
        WorkflowContextBindingRevision revision,
        ValidationResult result,
        bool requireExistingTenantRoles,
        CancellationToken cancellationToken)
    {
        if (!requireExistingTenantRoles)
            return;

        var expectedByRole = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        void AddExpected(string tenantRole, IEnumerable<string> capabilities)
        {
            if (string.IsNullOrWhiteSpace(tenantRole))
                return;
            if (!expectedByRole.TryGetValue(tenantRole, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                expectedByRole[tenantRole] = set;
            }

            foreach (var cap in capabilities.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                set.Add(ActivityAuthorization.MapCapability(
                    cap.Trim(),
                    revision.Definition.CapabilityOverrides,
                    revision.Definition.EventAliases));
            }
        }

        foreach (var role in source.Definition.Roles)
        {
            var tenantRole = TryGetValue(revision.Definition.RoleOverrides, role.Name, out var mapped)
                ? mapped
                : role.Name;
            AddExpected(tenantRole, role.GrantedCapabilities ?? new List<string>());
        }

        foreach (var step in source.Definition.Workflow.Steps)
        {
            var remappedCaps = ActivityAuthorization.NormalizeCapabilities(step.RequiredCapabilities)
                .Select(cap => ActivityAuthorization.MapCapability(
                    cap,
                    revision.Definition.CapabilityOverrides,
                    revision.Definition.EventAliases));

            var inboxRoles = (step.RequiredRoles ?? new List<string>())
                .Concat(step.AllowedRoles ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var templateRole in inboxRoles)
            {
                var tenantRole = TryGetValue(revision.Definition.RoleOverrides, templateRole, out var mapped)
                    ? mapped
                    : templateRole;
                AddExpected(tenantRole, remappedCaps);
            }

            foreach (var eventId in (step.NextSteps ?? new Dictionary<string, string>()).Keys)
            {
                if (string.IsNullOrWhiteSpace(eventId) ||
                    string.Equals(eventId, "Default", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(eventId, "true", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var match = source.Definition.Events.FirstOrDefault(evt =>
                    string.Equals(evt.EventId, eventId, StringComparison.OrdinalIgnoreCase));
                var eventCaps = ActivityAuthorization.NormalizeCapabilities(match?.RequiredCapabilities)
                    .Select(cap => ActivityAuthorization.MapCapability(
                        cap,
                        revision.Definition.CapabilityOverrides,
                        revision.Definition.EventAliases));
                foreach (var templateRole in inboxRoles)
                {
                    var tenantRole = TryGetValue(revision.Definition.RoleOverrides, templateRole, out var mapped)
                        ? mapped
                        : templateRole;
                    AddExpected(tenantRole, eventCaps);
                }
            }
        }

        foreach (var pair in expectedByRole)
        {
            if (pair.Value.Count == 0)
                continue;

            var tenantRole = await _unitOfWork.Roles.GetByNameAsync(tenantId, pair.Key, cancellationToken);
            if (tenantRole == null)
                continue;

            var granted = new HashSet<string>(tenantRole.Permissions, StringComparer.OrdinalIgnoreCase);
            var missing = pair.Value
                .Where(cap => !granted.Contains(cap))
                .OrderBy(cap => cap, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (missing.Count == 0)
                continue;

            result.AddError(
                "CTX-CAP-003",
                "Capabilities",
                $"Mapped tenant role '{pair.Key}' does not grant remapped capabilities: {string.Join(", ", missing)}.",
                $"Definition.RoleOverrides.{pair.Key}");
        }
    }

    private static void ValidateMappings(
        WorkflowClass source,
        WorkflowContextBindingRevision revision,
        ValidationResult result)
    {
        ValidateMapping(revision.Definition.InputMapping, "Definition.InputMapping", result);

        var declaredEvents = source.Definition.Events
            .Select(x => x.EventId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var eventMapping in revision.Definition.EventInputMappings)
        {
            if (!declaredEvents.Contains(eventMapping.Key))
            {
                result.AddError(
                    "CTX-MAP-003",
                    "Mapping",
                    $"Event input mapping references undeclared template event '{eventMapping.Key}'.",
                    $"Definition.EventInputMappings.{eventMapping.Key}");
            }
            ValidateMapping(
                eventMapping.Value,
                $"Definition.EventInputMappings.{eventMapping.Key}",
                result);
        }

        var mappedKeys = revision.Definition.InputMapping.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var eventMapping in revision.Definition.EventInputMappings.Values)
        {
            mappedKeys.UnionWith(eventMapping.Keys);
        }
        foreach (var parameter in revision.Definition.ConditionParameters)
        {
            if (!IsPublicCanonicalIdentifier(parameter.Key))
            {
                result.AddError(
                    "CTX-PARAM-001",
                    "Conditions",
                    $"Condition parameter '{parameter.Key}' is not a valid non-reserved canonical identifier.",
                    $"Definition.ConditionParameters.{parameter.Key}");
            }
            if (mappedKeys.Contains(parameter.Key))
            {
                result.AddError(
                    "CTX-PARAM-002",
                    "Conditions",
                    $"Condition parameter '{parameter.Key}' collides with an input-mapping target.",
                    $"Definition.ConditionParameters.{parameter.Key}");
            }
            if (!IsSerializable(parameter.Value))
            {
                result.AddError(
                    "CTX-PARAM-003",
                    "Conditions",
                    $"Condition parameter '{parameter.Key}' is not a serializable JSON value.",
                    $"Definition.ConditionParameters.{parameter.Key}");
            }
        }
    }

    private static void ValidateMapping(
        IReadOnlyDictionary<string, string> mapping,
        string element,
        ValidationResult result)
    {
        foreach (var item in mapping)
        {
            if (!IsPublicCanonicalIdentifier(item.Key))
            {
                result.AddError(
                    "CTX-MAP-001",
                    "Mapping",
                    $"Canonical field '{item.Key}' is not a valid non-reserved identifier.",
                    $"{element}.{item.Key}");
            }
            if (string.IsNullOrWhiteSpace(item.Value) || !SourcePathPattern.IsMatch(item.Value))
            {
                result.AddError(
                    "CTX-MAP-002",
                    "Mapping",
                    $"Source path '{item.Value}' is invalid. Only dotted object paths are supported.",
                    $"{element}.{item.Key}");
            }
        }
    }

    private static bool IsPublicCanonicalIdentifier(string value)
        => IdentifierPattern.IsMatch(value) &&
           !value.StartsWith("_", StringComparison.Ordinal) &&
           !ReservedCanonicalNames.Contains(value);

    private static bool IsSerializable(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined) return false;
        try
        {
            _ = value.GetRawText();
            _ = JsonSerializer.Serialize(value);
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private void ValidateDecisionProviders(
        WorkflowClass source,
        WorkflowContextBindingRevision revision,
        ValidationResult result)
    {
        var declared = source.Definition.Workflow.Steps
            .Select(x => x.DecisionProvider)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in revision.Definition.DecisionProviderOverrides)
        {
            if (!declared.Contains(provider.Key))
            {
                result.AddError(
                    "CTX-PLUGIN-001",
                    "Plugins",
                    $"Decision-provider override references undeclared provider '{provider.Key}'.",
                    $"Definition.DecisionProviderOverrides.{provider.Key}");
            }
            if (!_decisionPluginRegistry.TryResolve(provider.Value, out _))
            {
                result.AddError(
                    "CTX-PLUGIN-002",
                    "Plugins",
                    $"Decision provider '{provider.Value}' is not registered on this server.",
                    $"Definition.DecisionProviderOverrides.{provider.Key}");
            }
        }
    }

    private static void ValidateSchemas(
        WorkflowClass source,
        WorkflowContextBindingRevision revision,
        ValidationResult result)
    {
        var contextSchemaIsValid = ValidateJsonSchema(
            source.Definition.ContextSchema,
            "CTX-SCHEMA-001",
            "Template ContextSchema must be a valid object JSON schema.",
            "ContextSchema",
            result);
        var sourceSchemaIsValid = ValidateJsonSchema(
            revision.Definition.SourcePayloadSchema,
            "CTX-SCHEMA-002",
            "Source payload schema must be a valid object JSON schema.",
            "Definition.SourcePayloadSchema",
            result);

        if (contextSchemaIsValid)
        {
            ValidateCanonicalSchemaAlignment(source.Definition.ContextSchema, revision, result);
        }
        if (sourceSchemaIsValid)
        {
            ValidateSourceMappingAlignment(
                revision.Definition.SourcePayloadSchema,
                revision.Definition.InputMapping,
                "Definition.InputMapping",
                result);
        }

        var declaredEvents = source.Definition.Events
            .Select(x => x.EventId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var schema in revision.Definition.EventSourcePayloadSchemas)
        {
            if (!declaredEvents.Contains(schema.Key))
            {
                result.AddError(
                    "CTX-SCHEMA-003",
                    "Schema",
                    $"Event source schema references undeclared template event '{schema.Key}'.",
                    $"Definition.EventSourcePayloadSchemas.{schema.Key}");
            }
            var eventSchemaIsValid = ValidateJsonSchema(
                schema.Value,
                "CTX-SCHEMA-004",
                $"Source payload schema for event '{schema.Key}' must be a valid object JSON schema.",
                $"Definition.EventSourcePayloadSchemas.{schema.Key}",
                result);
            if (eventSchemaIsValid &&
                TryGetValue(revision.Definition.EventInputMappings, schema.Key, out var eventMapping))
            {
                ValidateSourceMappingAlignment(
                    schema.Value,
                    eventMapping,
                    $"Definition.EventInputMappings.{schema.Key}",
                    result);
            }
        }

        if (sourceSchemaIsValid)
        {
            foreach (var eventMapping in revision.Definition.EventInputMappings)
            {
                if (TryGetValue(
                        revision.Definition.EventSourcePayloadSchemas,
                        eventMapping.Key,
                        out _))
                {
                    continue;
                }

                ValidateSourceMappingAlignment(
                    revision.Definition.SourcePayloadSchema,
                    eventMapping.Value,
                    $"Definition.EventInputMappings.{eventMapping.Key}",
                    result);
            }
        }
    }

    private static bool ValidateJsonSchema(
        string? schema,
        string code,
        string message,
        string element,
        ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(schema)) return false;
        try
        {
            using var document = JsonDocument.Parse(schema);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.TryGetProperty("type", out var type) &&
                (type.ValueKind != JsonValueKind.String ||
                 !string.Equals(type.GetString(), "object", StringComparison.OrdinalIgnoreCase)) ||
                root.TryGetProperty("properties", out var properties) &&
                properties.ValueKind != JsonValueKind.Object ||
                root.TryGetProperty("required", out var required) &&
                (required.ValueKind != JsonValueKind.Array ||
                 required.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)))
            {
                result.AddError(code, "Schema", message, element);
                return false;
            }
            return true;
        }
        catch (JsonException)
        {
            result.AddError(code, "Schema", message, element);
            return false;
        }
    }

    private static void ValidateCanonicalSchemaAlignment(
        string? schema,
        WorkflowContextBindingRevision revision,
        ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(schema)) return;

        using var document = JsonDocument.Parse(schema);
        var root = document.RootElement;
        var availableAtStart = revision.Definition.InputMapping.Keys
            .Concat(revision.Definition.ConditionParameters.Keys)
            .ToHashSet(StringComparer.Ordinal);

        if (root.TryGetProperty("required", out var required))
        {
            foreach (var requiredProperty in required.EnumerateArray())
            {
                var name = requiredProperty.GetString()!;
                if (!availableAtStart.Contains(name))
                {
                    result.AddError(
                        "CTX-SCHEMA-005",
                        "Schema",
                        $"Required canonical field '{name}' must be provided by InputMapping or ConditionParameters.",
                        $"ContextSchema.required.{name}");
                }
            }
        }

        if (!root.TryGetProperty("properties", out var properties)) return;

        var projectedKeys = revision.Definition.InputMapping.Keys
            .Concat(revision.Definition.EventInputMappings.Values.SelectMany(x => x.Keys))
            .Concat(revision.Definition.ConditionParameters.Keys)
            .Distinct(StringComparer.Ordinal);
        foreach (var key in projectedKeys)
        {
            if (!properties.TryGetProperty(key, out _))
            {
                result.AddError(
                    "CTX-SCHEMA-006",
                    "Schema",
                    $"Canonical field '{key}' is not declared in the template ContextSchema.",
                    $"ContextSchema.properties.{key}");
            }
        }

        foreach (var parameter in revision.Definition.ConditionParameters)
        {
            if (parameter.Value.ValueKind == JsonValueKind.Undefined ||
                !properties.TryGetProperty(parameter.Key, out var propertySchema) ||
                MatchesSchemaType(propertySchema, parameter.Value))
            {
                continue;
            }

            result.AddError(
                "CTX-SCHEMA-007",
                "Schema",
                $"Condition parameter '{parameter.Key}' does not match its canonical schema type.",
                $"Definition.ConditionParameters.{parameter.Key}");
        }
    }

    private static void ValidateSourceMappingAlignment(
        string? schema,
        IReadOnlyDictionary<string, string> mapping,
        string element,
        ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(schema) || mapping.Count == 0) return;

        using var document = JsonDocument.Parse(schema);
        if (!document.RootElement.TryGetProperty("properties", out _)) return;

        foreach (var item in mapping)
        {
            if (TryResolveSchemaPath(document.RootElement, item.Value, out _)) continue;

            result.AddError(
                "CTX-SCHEMA-008",
                "Schema",
                $"Source path '{item.Value}' is not declared in its source payload schema.",
                $"{element}.{item.Key}");
        }
    }

    private static bool TryResolveSchemaPath(
        JsonElement schema,
        string path,
        out JsonElement propertySchema)
    {
        propertySchema = schema;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!propertySchema.TryGetProperty("properties", out var properties) ||
                !TryGetProperty(properties, segment, out propertySchema))
            {
                propertySchema = default;
                return false;
            }
        }
        return true;
    }

    private static bool TryGetProperty(
        JsonElement properties,
        string name,
        out JsonElement value)
    {
        foreach (var property in properties.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static bool MatchesSchemaType(JsonElement schema, JsonElement value)
    {
        if (!schema.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String)
        {
            return true;
        }

        return type.GetString()?.ToLowerInvariant() switch
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
    }

    private static bool TryGetValue<T>(
        IReadOnlyDictionary<string, T> values,
        string key,
        out T value)
    {
        foreach (var item in values)
        {
            if (!string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
            value = item.Value;
            return true;
        }
        value = default!;
        return false;
    }
}
