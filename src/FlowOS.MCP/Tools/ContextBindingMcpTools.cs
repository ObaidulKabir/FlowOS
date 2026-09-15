using System.Text.Json;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
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
        PropertyNameCaseInsensitive = true
    };

    private readonly IMediator _mediator;

    public ContextBindingMcpTools(IMediator mediator)
    {
        _mediator = mediator;
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
                ex.Message,
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
