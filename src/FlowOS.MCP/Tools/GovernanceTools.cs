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
            var version = args["version"]?.ToString() ?? "1.0.0";
            var blueprintJson = args["blueprint"] as JObject;

            if (string.IsNullOrWhiteSpace(name)) return McpToolResults.Fail("MCP-ARG-001", "name is required.");
            if (blueprintJson == null) return McpToolResults.Fail("MCP-ARG-001", "blueprint is required.");

            var tenantId = McpTenantResolver.ResolveRequired(args);

            var blueprint = blueprintJson.ToObject<WorkflowClassBlueprint>();
            if (blueprint == null) return McpToolResults.Fail("MCP-ARG-001", "blueprint format is invalid.");

            var result = await _mediator.Send(new CreateWorkflowClassCommand(tenantId, name, version, blueprint));
            return McpToolResults.Success(new { id = result.Id, tenantId, status = "Draft", message = "Draft created successfully" });
        }
        catch (WorkflowClassValidationException ex)
        {
            return McpToolResults.ValidationFailed(ex.ValidationResult);
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to create draft.");
        }
    }

    public async Task<CallToolResult> UpdateDraft(JObject args)
    {
        try
        {
            var idStr = args["id"]?.ToString();
            var blueprintJson = args["blueprint"] as JObject;

            if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out var id))
                return McpToolResults.Fail("MCP-ARG-002", "id must be a valid UUID.");
            if (blueprintJson == null) return McpToolResults.Fail("MCP-ARG-001", "blueprint is required.");

            var tenantId = McpTenantResolver.ResolveRequired(args);

            var existing = await _mediator.Send(new GetWorkflowClassByIdQuery(tenantId, id));
            if (existing == null) return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found.");

            var name = args["name"]?.ToString() ?? existing.Name;
            var version = args["version"]?.ToString() ?? existing.Version;

            var blueprint = blueprintJson.ToObject<WorkflowClassBlueprint>();
            if (blueprint == null) return McpToolResults.Fail("MCP-ARG-001", "blueprint format is invalid.");

            var result = await _mediator.Send(new UpdateWorkflowClassCommand(tenantId, id, name, version, blueprint));
            return McpToolResults.Success(new { id = result.Id, status = result.Status.ToString(), message = "Draft updated successfully" });
        }
        catch (WorkflowClassValidationException ex)
        {
            return McpToolResults.ValidationFailed(ex.ValidationResult);
        }
        catch (KeyNotFoundException)
        {
            return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found.");
        }
        catch (UnauthorizedAccessException)
        {
            return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found.");
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to update draft.");
        }
    }

    public async Task<CallToolResult> ValidateDraft(JObject args)
    {
        try
        {
            var idStr = args["id"]?.ToString();
            if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out var id))
                return McpToolResults.Fail("MCP-ARG-002", "id must be a valid UUID.");

            var tenantId = McpTenantResolver.ResolveRequired(args);

            var result = await _mediator.Send(new ValidateWorkflowClassCommand(tenantId, id));
            return McpToolResults.Success(new
            {
                isValid = result.IsValid,
                errors = result.Errors.Select(e => new
                {
                    code = e.Code,
                    category = e.Category,
                    message = e.Message,
                    element = e.Element
                })
            });
        }
        catch (KeyNotFoundException)
        {
            return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found.");
        }
        catch (UnauthorizedAccessException)
        {
            return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found.");
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Draft validation failed.");
        }
    }

    public async Task<CallToolResult> ForkPublic(JObject args)
    {
        try
        {
            var publicIdStr = args["publicId"]?.ToString();

            if (string.IsNullOrEmpty(publicIdStr) || !Guid.TryParse(publicIdStr, out var publicId))
                return McpToolResults.Fail("MCP-ARG-002", "publicId must be a valid UUID.");

            var tenantId = McpTenantResolver.ResolveRequired(args);

            var result = await _mediator.Send(new CopyWorkflowClassCommand(tenantId, publicId, tenantId));
            return McpToolResults.Success(new { id = result.Id, tenantId, status = "Draft", message = $"Forked from {result.Name}" });
        }
        catch (KeyNotFoundException)
        {
            return McpToolResults.Fail("MCP-NOTFOUND-001", "Public WorkflowClass not found.");
        }
        catch (InvalidOperationException)
        {
            return McpToolResults.Fail("MCP-ARG-001", "WorkflowClass cannot be forked.");
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "WorkflowClass fork failed.");
        }
    }

    public async Task<CallToolResult> GetDraft(JObject args)
    {
        try
        {
            var idStr = args["id"]?.ToString();
            if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out var id))
                return McpToolResults.Fail("MCP-ARG-002", "id must be a valid UUID.");

            var tenantId = McpTenantResolver.ResolveRequired(args);

            var workflowClass = await _mediator.Send(new GetWorkflowClassByIdQuery(tenantId, id));
            if (workflowClass == null)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found.");
            }

            return McpToolResults.Success(workflowClass);
        }
        catch (UnauthorizedAccessException)
        {
            return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowClass not found.");
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to retrieve draft workflow class.");
        }
    }

    public async Task<CallToolResult> ListDrafts(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            var list = await _mediator.Send(new ListWorkflowClassesQuery(tenantId, null, FlowOS.Domain.Enums.WorkflowClassStatus.Draft));
            var drafts = list.Select(w => new
            {
                w.Id,
                w.Name,
                w.Version,
                Status = w.Status.ToString(),
                Scope = w.Scope.ToString(),
                w.CreatedAt,
                w.PublishedAt
            }).ToList();

            return McpToolResults.Success(new { drafts });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to list draft workflow classes.");
        }
    }
}
