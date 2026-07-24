using FlowOS.Application.Commands.Governance;
using FlowOS.Application.Handlers.Governance;
using FlowOS.Application.Queries.Governance;
using FlowOS.Domain.Blueprints;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using MediatR;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class GovernanceTools
{
    private readonly IMediator _mediator;

    public GovernanceTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<CallToolResult> CreateDraft(JObject args)
    {
        try
        {
            var name = args["name"]?.ToString();
            var version = args["version"]?.ToString() ?? "0.1.0";
            var blueprintJson = args["blueprint"] as JObject;
            var tenantIdStr = args["tenantId"]?.ToString();

            if (string.IsNullOrEmpty(name)) return Error("Name is required");
            if (blueprintJson == null) return Error("Blueprint is required");

            var tenantId = Guid.TryParse(tenantIdStr, out var tid) ? tid : Guid.NewGuid();
            McpRequestContext.TenantId = tenantId;

            var blueprint = blueprintJson.ToObject<WorkflowClassBlueprint>();
            if (blueprint == null) return Error("Invalid blueprint format");

            var result = await _mediator.Send(new CreateWorkflowClassCommand(tenantId, name, version, blueprint));
            return Success(new { id = result.Id, tenantId, status = "Draft", message = "Draft created successfully" });
        }
        catch (WorkflowClassValidationException ex)
        {
            return Error($"Validation Failed: {string.Join(", ", ex.ValidationResult.Errors.Select(e => $"{e.Code}: {e.Message}"))}");
        }
        catch (Exception ex)
        {
            return Error($"Failed to create draft: {ex.Message}");
        }
    }

    public async Task<CallToolResult> UpdateDraft(JObject args)
    {
        try
        {
            var idStr = args["id"]?.ToString();
            var blueprintJson = args["blueprint"] as JObject;

            if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out var id)) return Error("Valid ID is required");
            if (blueprintJson == null) return Error("Blueprint is required");

            var tenantIdStr = args["tenantId"]?.ToString();
            if (!Guid.TryParse(tenantIdStr, out var tenantId))
                return Error("tenantId is required for update_draft_workflowclass");

            McpRequestContext.TenantId = tenantId;

            var existing = await _mediator.Send(new GetWorkflowClassByIdQuery(tenantId, id));
            if (existing == null) return Error("WorkflowClass not found");

            var name = args["name"]?.ToString() ?? existing.Name;
            var version = args["version"]?.ToString() ?? existing.Version;

            var blueprint = blueprintJson.ToObject<WorkflowClassBlueprint>();
            if (blueprint == null) return Error("Invalid blueprint format");

            var result = await _mediator.Send(new UpdateWorkflowClassCommand(tenantId, id, name, version, blueprint));
            return Success(new { id = result.Id, status = result.Status.ToString(), message = "Draft updated successfully" });
        }
        catch (WorkflowClassValidationException ex)
        {
            return Error($"Validation Failed: {string.Join(", ", ex.ValidationResult.Errors.Select(e => $"{e.Code}: {e.Message}"))}");
        }
        catch (KeyNotFoundException)
        {
            return Error("WorkflowClass not found");
        }
        catch (UnauthorizedAccessException)
        {
            return Error("WorkflowClass is not owned by the current tenant.");
        }
        catch (Exception ex)
        {
            return Error($"Failed to update draft: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ValidateDraft(JObject args)
    {
        try
        {
            var idStr = args["id"]?.ToString();
            if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out var id)) return Error("Valid ID is required");

            var tenantIdStr = args["tenantId"]?.ToString();
            if (!Guid.TryParse(tenantIdStr, out var tenantId))
                return Error("tenantId is required for validate_draft_workflowclass");

            McpRequestContext.TenantId = tenantId;

            var result = await _mediator.Send(new ValidateWorkflowClassCommand(tenantId, id));
            return Success(new
            {
                isValid = result.IsValid,
                errors = result.Errors.Select(e => new { code = e.Code, message = e.Message })
            });
        }
        catch (KeyNotFoundException)
        {
            return Error("WorkflowClass not found");
        }
        catch (UnauthorizedAccessException)
        {
            return Error("WorkflowClass is not owned by the current tenant.");
        }
        catch (Exception ex)
        {
            return Error($"Validation failed: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ForkPublic(JObject args)
    {
        try
        {
            var publicIdStr = args["publicId"]?.ToString();
            var tenantIdStr = args["tenantId"]?.ToString();

            if (string.IsNullOrEmpty(publicIdStr) || !Guid.TryParse(publicIdStr, out var publicId))
                return Error("Valid Public ID is required");

            var tenantId = Guid.TryParse(tenantIdStr, out var tid) ? tid : Guid.NewGuid();
            McpRequestContext.TenantId = tenantId;

            var result = await _mediator.Send(new CopyWorkflowClassCommand(tenantId, publicId, tenantId));
            return Success(new { id = result.Id, tenantId, status = "Draft", message = $"Forked from {result.Name}" });
        }
        catch (KeyNotFoundException)
        {
            return Error("Public WorkflowClass not found");
        }
        catch (InvalidOperationException ex)
        {
            return Error(ex.Message);
        }
        catch (Exception ex)
        {
            return Error($"Fork failed: {ex.Message}");
        }
    }

    private static CallToolResult Success(object data) => new()
    {
        Content = new List<ToolContent>
        {
            new ToolContent { Type = "json", Text = JObject.FromObject(data).ToString() }
        }
    };

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = new List<ToolContent>
        {
            new ToolContent { Type = "text", Text = message }
        }
    };
}
