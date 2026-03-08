using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using FlowOS.Infrastructure.Persistence;
using FlowOS.MCP.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FlowOS.MCP.Tools
{
    public class AgentTools
    {
        private readonly FlowOSDbContext _dbContext;

        public AgentTools(FlowOSDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<CallToolResult> ListAvailableAgents(JObject args)
        {
            // In a real system, this would come from an IAgentRegistry
            var agents = new List<object>
            {
                new 
                { 
                    id = "RiskAnalysisAgent", 
                    name = "Risk Analyzer", 
                    description = "Analyzes expense data for high-value risks and fraud patterns.",
                    capabilities = new[] { "EVT-ESCALATE", "EVT-APPROVE" }
                }
            };

            return Task.FromResult(new CallToolResult
            {
                Content = new List<ToolContent>
                {
                    new ToolContent { Type = "json", Text = JObject.FromObject(new { agents }).ToString() }
                }
            });
        }

        public async Task<CallToolResult> SuggestAgentAction(JObject args)
        {
            try
            {
                var instanceIdStr = args["workflowInstanceId"]?.ToString();
                var agentId = args["agentId"]?.ToString();

                if (string.IsNullOrEmpty(instanceIdStr) || !Guid.TryParse(instanceIdStr, out var instanceId))
                    return Error("Valid WorkflowInstanceId is required");

                if (string.IsNullOrEmpty(agentId))
                    return Error("AgentId is required");
                
                var instance = await _dbContext.WorkflowInstances.FindAsync(instanceId);
                if (instance == null) return Error("WorkflowInstance not found");

                // Mocking the context load and execution for the MCP demo
                // Ideally, we inject IAgentService here.
                
                IWorkflowAgent agent = null;
                if (agentId == "RiskAnalysisAgent") agent = new RiskAnalysisAgent();
                else return Error($"Agent '{agentId}' not found");

                // Use the new payload logic to try and extract real data if possible
                // For MCP demo, if no events exist, fallback to simulation
                var payload = new Dictionary<string, object> 
                { 
                    { "Amount", 6000 }, // Simulated high value to trigger suggestion
                    { "Category", "Travel" } 
                };

                // Try to find payload from latest event?
                var lastEvent = await _dbContext.Events
                    .Where(e => e.CorrelationId == instanceId)
                    .OrderByDescending(e => e.Timestamp)
                    .FirstOrDefaultAsync();
                
                if (lastEvent != null && lastEvent.Metadata.ContainsKey("Payload"))
                {
                    try 
                    {
                        var json = lastEvent.Metadata["Payload"].ToString();
                        var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        if (dict != null) payload = dict;
                    }
                    catch { /* Ignore parsing error */ }
                }

                var context = new AgentContext(
                    instance.TenantId,
                    payload,
                    instance.CurrentStepId,
                    new List<FlowOS.Events.Abstractions.IEvent>(), // Empty history for now
                    "Analyze for MCP"
                );

                var result = await agent.ExecuteAsync(context);

                return new CallToolResult
                {
                    Content = new List<ToolContent>
                    {
                        new ToolContent { Type = "json", Text = JObject.FromObject(result).ToString() }
                    }
                };
            }
            catch (Exception ex)
            {
                return Error($"Agent execution failed: {ex.Message}");
            }
        }

        private CallToolResult Error(string message)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = new List<ToolContent>
                {
                    new ToolContent { Type = "text", Text = message }
                }
            };
        }
    }
}
