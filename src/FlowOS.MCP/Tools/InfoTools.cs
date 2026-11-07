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
            // DX report F7: every other instance-scoped tool (start_workflow's response, publish_event,
            // ...) uses workflowInstanceId - this was the one that only accepted instanceId/id,
            // forcing a round-trip to discover the mismatch. workflowInstanceId is now the canonical
            // name; instanceId/id are accepted as deprecated synonyms rather than removed outright.
            var instanceIdStr = args["workflowInstanceId"]?.ToString()
                ?? args["instanceId"]?.ToString()
                ?? args["id"]?.ToString();
            if (string.IsNullOrEmpty(instanceIdStr) || !Guid.TryParse(instanceIdStr, out var instanceId))
            {
                var received = string.Join(", ", args.Properties().Select(p => p.Name));
                return McpToolResults.Fail(
                    "MCP-ARG-002",
                    $"workflowInstanceId must be a valid UUID (accepts instanceId/id as deprecated synonyms). Received arguments: [{received}].");
            }

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
