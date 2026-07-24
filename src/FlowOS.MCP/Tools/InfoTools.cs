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
        // Mirrors FlowOS.Domain.Blueprints.WorkflowClassBlueprint (verified against source).
        var schema = new
        {
            type = "object",
            properties = new
            {
                Events = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            EventId = new { type = "string" },
                            Name = new { type = "string" },
                            Description = new { type = "string" },
                            Category = new { type = "string", @enum = new[] { "System", "User", "Temporal", "External" } },
                            IsTerminal = new { type = "boolean" },
                            PayloadSchema = new { type = "string", description = "Optional JSON Schema for event payload validation" }
                        }
                    }
                },
                StateMachine = new
                {
                    type = "object",
                    properties = new
                    {
                        EntityType = new { type = "string" },
                        InitialState = new { type = "string" },
                        States = new { type = "array", items = new { type = "string" } },
                        Transitions = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    FromState = new { type = "string" },
                                    ToState = new { type = "string" },
                                    EventId = new { type = "string" }
                                }
                            }
                        }
                    }
                },
                Workflow = new
                {
                    type = "object",
                    properties = new
                    {
                        StartStepId = new { type = "string" },
                        Steps = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    StepId = new { type = "string" },
                                    StepType = new { type = "string", @enum = new[] { "Command", "HumanTask", "SystemTask", "Decision", "Timer", "End" } },
                                    NextSteps = new { type = "object", description = "Map of EventId -> NextStepId" },
                                    RequiredRoles = new { type = "array", items = new { type = "string" } },
                                    Conditions = new { type = "object", description = "For Decision steps: Condition -> NextStepId" }
                                }
                            }
                        }
                    }
                },
                Roles = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            Name = new { type = "string" },
                            Description = new { type = "string" },
                            GrantedCapabilities = new { type = "array", items = new { type = "string" } }
                        }
                    }
                },
                Capabilities = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            Code = new { type = "string" },
                            Description = new { type = "string" }
                        }
                    }
                }
            }
        };

        return Task.FromResult(new CallToolResult
        {
            Content = new List<ToolContent>
            {
                new ToolContent { Type = "json", Text = JObject.FromObject(schema).ToString() }
            }
        });
    }

    public async Task<CallToolResult> ListPublic(JObject args)
    {
        var tenantIdStr = args["tenantId"]?.ToString();
        var tenantId = Guid.TryParse(tenantIdStr, out var tid)
            ? tid
            : Guid.Parse("11111111-1111-1111-1111-111111111111");
        McpRequestContext.TenantId = tenantId;

        var list = await _mediator.Send(new ListWorkflowClassesQuery(tenantId, WorkflowClassScope.Public, null));
        var publicWorkflows = list.Select(w => new { w.Id, w.Name, w.Version }).ToList();

        return new CallToolResult
        {
            Content = new List<ToolContent>
            {
                new ToolContent { Type = "json", Text = JObject.FromObject(publicWorkflows).ToString() }
            }
        };
    }
}
