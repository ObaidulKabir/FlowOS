using FlowOS.Application.Queries.Governance;
using FlowOS.Domain.Enums;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using MediatR;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class InfoTools
{
    private readonly IMediator _mediator;

    public InfoTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    public Task<CallToolResult> DescribeSchema(JObject args)
    {
        return Task.FromResult(McpToolResults.Success(McpToolSchemas.BlueprintSchema()));
    }

    public async Task<CallToolResult> ListPublic(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var list = await _mediator.Send(new ListWorkflowClassesQuery(tenantId, WorkflowClassScope.Public, null));
            var publicWorkflows = list.Select(w => new { w.Id, w.Name, w.Version }).ToList();

            return McpToolResults.Success(new { workflowClasses = publicWorkflows });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
    }

    public async Task<CallToolResult> GetWorkflowInstanceStatus(JObject args)
    {
        try
        {
            var instanceIdStr = args["instanceId"]?.ToString() ?? args["id"]?.ToString();
            if (string.IsNullOrEmpty(instanceIdStr) || !Guid.TryParse(instanceIdStr, out var instanceId))
                return McpToolResults.Fail("MCP-ARG-002", "instanceId must be a valid UUID.");

            var tenantId = McpTenantResolver.ResolveRequired(args);

            var summary = await _mediator.Send(new FlowOS.Application.Queries.GetWorkflowByIdQuery
            {
                TenantId = tenantId,
                Id = instanceId
            });

            if (summary == null)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", "Workflow instance not found.");
            }

            return McpToolResults.Success(summary);
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to retrieve workflow instance status.");
        }
    }
}
