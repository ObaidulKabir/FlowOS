using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using MediatR;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class ContextBindingMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IMediator _mediator;
    private readonly IUnitOfWork? _unitOfWork;

    public ContextBindingMcpTools(IMediator mediator, IUnitOfWork? unitOfWork = null)
    {
        _mediator = mediator;
        _unitOfWork = unitOfWork;
    }

    public Task<CallToolResult> Create(JObject args)
        => Execute(async () =>
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var sourceId = RequiredGuid(args, "sourceWorkflowClassId");
            var contextType = RequiredString(args, "contextType");
            var name = RequiredString(args, "name");
            var definition = RequiredDefinition(args);
            var result = await _mediator.Send(new CreateWorkflowContextBindingCommand(
                tenantId, sourceId, contextType, name, definition));
            return AsToken(result);
        }, "create context binding");

    public Task<CallToolResult> Update(JObject args)
        => Execute(async () =>
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var id = RequiredGuid(args, "id");
            var definition = RequiredDefinition(args);
            Guid? sourceId = null;
            if (args["sourceWorkflowClassId"] != null)
            {
                sourceId = RequiredGuid(args, "sourceWorkflowClassId");
            }
            var result = await _mediator.Send(new UpdateWorkflowContextBindingCommand(
                tenantId, id, definition, sourceId));
            return AsToken(result);
        }, "update context binding");

    public Task<CallToolResult> Validate(JObject args)
        => Execute(async () =>
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var id = RequiredGuid(args, "id");
            var result = await _mediator.Send(
                new ValidateWorkflowContextBindingCommand(tenantId, id));
            return AsToken(result);
        }, "validate context binding");

    public Task<CallToolResult> Activate(JObject args)
        => Execute(async () =>
        {
            RequireHumanApproval(args, "activate_context_binding");
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var id = RequiredGuid(args, "id");
            var result = await _mediator.Send(
                new ActivateWorkflowContextBindingCommand(tenantId, id));
            return AsToken(result);
        }, "activate context binding");

    public Task<CallToolResult> Archive(JObject args)
        => Execute(async () =>
        {
            RequireHumanApproval(args, "archive_context_binding");
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var id = RequiredGuid(args, "id");
            var result = await _mediator.Send(
                new ArchiveWorkflowContextBindingCommand(tenantId, id));
            return AsToken(result);
        }, "archive context binding");

    public Task<CallToolResult> List(JObject args)
        => Execute(async () =>
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            Guid? sourceId = null;
            if (args["sourceWorkflowClassId"] != null)
            {
                sourceId = RequiredGuid(args, "sourceWorkflowClassId");
            }
            var result = await _mediator.Send(
                new ListWorkflowContextBindingsQuery(tenantId, sourceId));
            return new JObject
            {
                ["totalCount"] = result.Count,
                ["contextBindings"] = AsToken(result)
            };
        }, "list context bindings");

    public Task<CallToolResult> Get(JObject args)
        => Execute(async () =>
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var id = RequiredGuid(args, "id");
            var result = await _mediator.Send(
                new GetWorkflowContextBindingQuery(tenantId, id));
            if (result == null)
                throw new KeyNotFoundException("Workflow context binding was not found.");
            return AsToken(result);
        }, "get context binding");

    public Task<CallToolResult> Simulate(JObject args)
        => Execute(async () =>
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            WorkflowContextSimulationRequest request;
            try
            {
                request = JsonSerializer.Deserialize<WorkflowContextSimulationRequest>(
                              args.ToString(Newtonsoft.Json.Formatting.None),
                              JsonOptions)
                          ?? throw new ArgumentException("Simulation request is invalid.");
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Simulation request is invalid.", ex);
            }

            var result = await _mediator.Send(
                new SimulateWorkflowContextBindingQuery(tenantId, request));
            return AsToken(result);
        }, "simulate context binding");

    public Task<CallToolResult> InspectContextSchema(JObject args)
        => Execute(async () =>
        {
            string? schemaJson = null;

            if (args["schema"] != null)
            {
                schemaJson = args["schema"]!.ToString(Newtonsoft.Json.Formatting.None);
            }
            else if (_unitOfWork != null && args["workflowClassId"] != null && Guid.TryParse(args["workflowClassId"]?.ToString(), out var clsId))
            {
                var cls = await _unitOfWork.WorkflowClasses.GetByIdAsNoTrackingAsync(clsId);
                if (cls == null) throw new KeyNotFoundException($"WorkflowClass '{clsId}' was not found.");
                schemaJson = cls.Definition.ContextSchema;
            }
            else if (_unitOfWork != null && args["contextBindingId"] != null && Guid.TryParse(args["contextBindingId"]?.ToString(), out var bId))
            {
                var tenantId = McpTenantResolver.ResolveRequired(args);
                var binding = await _unitOfWork.WorkflowContextBindings.GetByIdAsNoTrackingAsync(bId, tenantId);
                if (binding == null) throw new KeyNotFoundException($"Context binding '{bId}' was not found.");
                var revId = binding.ActiveRevisionId ?? binding.DraftRevisionId;
                if (revId.HasValue)
                {
                    var rev = await _unitOfWork.WorkflowContextBindings.GetRevisionByIdAsNoTrackingAsync(revId.Value);
                    if (rev != null)
                    {
                        var cls = await _unitOfWork.WorkflowClasses.GetByIdAsNoTrackingAsync(rev.SourceWorkflowClassId);
                        if (cls != null) schemaJson = cls.Definition.ContextSchema;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(schemaJson))
            {
                return new JObject
                {
                    ["hasSchema"] = false,
                    ["message"] = "No context schema found for the given reference or arguments."
                };
            }

            var isParsed = ContextSchemaParser.TryParse(schemaJson, out var parsedSchema);

            var result = new JObject
            {
                ["hasSchema"] = true,
                ["isDeclarative"] = isParsed && parsedSchema != null && (parsedSchema.Fields.Count > 0 || !string.IsNullOrEmpty(parsedSchema.EntityType)),
                ["parsedSchema"] = parsedSchema != null ? AsToken(parsedSchema) : null,
                ["rawSchema"] = schemaJson
            };
            return result;
        }, "inspect context schema");

    public Task<CallToolResult> InspectInstanceContext(JObject args)
        => Execute(async () =>
        {
            if (_unitOfWork == null)
                throw new InvalidOperationException("Unit of work is not available for context inspection.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var instanceId = RequiredGuid(args, "workflowInstanceId");

            var snapshot = await _unitOfWork.WorkflowContextSnapshots.GetAsync(instanceId, tenantId);
            if (snapshot == null)
            {
                throw new KeyNotFoundException($"Workflow context snapshot for instance '{instanceId}' was not found.");
            }

            var result = new JObject
            {
                ["workflowInstanceId"] = snapshot.WorkflowInstanceId.ToString(),
                ["tenantId"] = snapshot.TenantId.ToString(),
                ["contextBindingRevisionId"] = snapshot.ContextBindingRevisionId.ToString(),
                ["identity"] = new JObject
                {
                    ["entityType"] = snapshot.EntityType,
                    ["entityId"] = snapshot.EntityId,
                    ["correlationKeys"] = AsToken(snapshot.CorrelationKeys),
                    ["sourceSystem"] = snapshot.SourceSystem,
                    ["externalEntityId"] = snapshot.ExternalEntityId
                },
                ["businessData"] = AsToken(snapshot.CanonicalData),
                ["systemData"] = AsToken(snapshot.SystemData),
                ["resolvedRoles"] = AsToken(snapshot.ResolvedRoles),
                ["concurrencyVersion"] = snapshot.ConcurrencyVersion,
                ["createdAtUtc"] = snapshot.CreatedAtUtc.ToString("O"),
                ["updatedAtUtc"] = snapshot.UpdatedAtUtc.ToString("O")
            };
            return result;
        }, "inspect instance context");

    public Task<CallToolResult> TestContextOperations(JObject args)
        => Execute(async () =>
        {
            await Task.CompletedTask;
            ContextSchemaDefinition? schema = null;
            if (args["schema"] != null)
            {
                ContextSchemaParser.TryParse(args["schema"]!.ToString(Newtonsoft.Json.Formatting.None), out schema);
            }

            var initialData = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (args["baseContext"] is JObject baseObj)
            {
                using var doc = JsonDocument.Parse(baseObj.ToString(Newtonsoft.Json.Formatting.None));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    initialData[prop.Name] = prop.Value.Clone();
                }
            }

            var snapshot = new WorkflowContextSnapshot(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                initialData,
                schema: schema);

            var opsList = new List<ContextOperation>();
            if (args["operations"] is JArray opsArray)
            {
                foreach (var opToken in opsArray.OfType<JObject>())
                {
                    var typeStr = opToken["op"]?.ToString() ?? opToken["type"]?.ToString() ?? "Set";
                    if (!Enum.TryParse<ContextOperationType>(typeStr, true, out var opType))
                    {
                        opType = ContextOperationType.Set;
                    }
                    var field = opToken["field"]?.ToString()?.Trim() ?? string.Empty;
                    var valToken = opToken["value"];
                    JsonElement elem = default;
                    if (valToken != null)
                    {
                        using var valDoc = JsonDocument.Parse(valToken.ToString(Newtonsoft.Json.Formatting.None));
                        elem = valDoc.RootElement.Clone();
                    }

                    opsList.Add(new ContextOperation
                    {
                        Type = opType,
                        Field = field,
                        Value = elem
                    });
                }
            }

            snapshot.ApplyOperations(opsList, schema, actor: args["actor"]?.ToString() ?? "McpSimulator");

            return new JObject
            {
                ["success"] = true,
                ["entityType"] = snapshot.EntityType,
                ["entityId"] = snapshot.EntityId,
                ["concurrencyVersion"] = snapshot.ConcurrencyVersion,
                ["canonicalData"] = AsToken(snapshot.CanonicalData),
                ["systemData"] = AsToken(snapshot.SystemData)
            };
        }, "test context operations");

    private static async Task<CallToolResult> Execute(
        Func<Task<JToken>> action,
        string operation)
    {
        try
        {
            return McpToolResults.Success(await action());
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (WorkflowContextBindingValidationException ex)
        {
            return McpToolResults.Fail(
                "MCP-VALIDATION",
                "Workflow context binding validation failed.",
                ex.ValidationResult.Errors);
        }
        catch (WorkflowContextPayloadException ex)
        {
            return McpToolResults.Fail(
                "MCP-VALIDATION",
                "Context payload validation failed.",
                ex.Errors);
        }
        catch (KeyNotFoundException ex)
        {
            return McpToolResults.Fail("MCP-NOTFOUND-001", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return McpToolResults.Fail("CTX-STATE-001", ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to {operation}: {ex.Message}");
        }
    }

    private static Guid RequiredGuid(JObject args, string property)
    {
        if (!Guid.TryParse(args[property]?.ToString(), out var value) || value == Guid.Empty)
            throw new ArgumentException($"{property} must be a valid UUID.");
        return value;
    }

    private static void RequireHumanApproval(JObject args, string operation)
    {
        if (args["confirmHumanApproval"]?.Value<bool>() == true) return;

        throw new McpToolException(
            "MCP-APPROVAL-REQUIRED",
            $"High-risk operation '{operation}' requires explicit human confirmation. Supply 'confirmHumanApproval: true' in arguments to execute.");
    }

    private static string RequiredString(JObject args, string property)
    {
        var value = args[property]?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{property} is required.");
        return value;
    }

    private static WorkflowContextBindingDefinition RequiredDefinition(JObject args)
    {
        if (args["definition"] is not JObject definition)
            throw new ArgumentException("definition is required.");
        try
        {
            return JsonSerializer.Deserialize<WorkflowContextBindingDefinition>(
                       definition.ToString(Newtonsoft.Json.Formatting.None),
                       JsonOptions)
                   ?? throw new ArgumentException("definition is invalid.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("definition is invalid.", ex);
        }
    }

    private static JToken AsToken(object value)
        => JToken.Parse(JsonSerializer.Serialize(value, JsonOptions));
}
