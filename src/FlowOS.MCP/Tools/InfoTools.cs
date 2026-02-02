using FlowOS.Infrastructure.Persistence;
using FlowOS.MCP.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Domain.Enums;

namespace FlowOS.MCP.Tools
{
    public class InfoTools
    {
        private readonly FlowOSDbContext _dbContext;

        public InfoTools(FlowOSDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<CallToolResult> DescribeSchema(JObject args)
        {
            // In a real implementation, we would generate this via NJsonSchema or similar.
            // For this implementation, we provide a description of the key fields.
            
            var schema = new
            {
                type = "object",
                properties = new
                {
                    Events = new 
                    { 
                        type = "array", 
                        items = new { 
                            type = "object", 
                            properties = new { 
                                EventType = new { type = "string" }, 
                                Category = new { type = "string", @enum = new[] { "System", "User", "Temporal", "External" } } 
                            } 
                        } 
                    },
                    StateMachine = new 
                    { 
                        type = "object", 
                        properties = new { 
                            InitialState = new { type = "string" },
                            States = new { type = "array", items = new { type = "string" } },
                            Transitions = new { 
                                type = "array", 
                                items = new { 
                                    type = "object", 
                                    properties = new {
                                        FromState = new { type = "string" },
                                        EventType = new { type = "string" },
                                        ToState = new { type = "string" }
                                    }
                                } 
                            }
                        } 
                    },
                    Workflow = new 
                    { 
                        type = "object", 
                        properties = new { 
                            StartStepId = new { type = "string" },
                            Steps = new { 
                                type = "array", 
                                items = new { 
                                    type = "object", 
                                    properties = new {
                                        StepId = new { type = "string" },
                                        StepType = new { type = "string", @enum = new[] { "HumanTask", "SystemTask", "Decision", "EventWait" } },
                                        Label = new { type = "string" },
                                        Config = new { type = "object" },
                                        NextSteps = new { type = "object", description = "Map of EventType -> NextStepId" }
                                    }
                                } 
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
            var publicWorkflows = await _dbContext.WorkflowClasses
                .Where(w => w.Scope == WorkflowClassScope.Public)
                .Select(w => new { w.Id, w.Name, w.Version })
                .ToListAsync();

            return new CallToolResult
            {
                Content = new List<ToolContent>
                {
                    new ToolContent { Type = "json", Text = JObject.FromObject(publicWorkflows).ToString() }
                }
            };
        }
    }
}
