using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.MCP.Models;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class AgentTools
{
    private readonly IUnitOfWork _unitOfWork;

    public AgentTools(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<CallToolResult> ListAvailableAgents(JObject args)
    {
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

            var instance = await _unitOfWork.WorkflowInstances.GetByIdAsNoTrackingAsync(instanceId);
            if (instance == null) return Error("WorkflowInstance not found");

            IWorkflowAgent? agent = agentId == "RiskAnalysisAgent" ? new RiskAnalysisAgent() : null;
            if (agent == null) return Error($"Agent '{agentId}' not found");

            var payload = new Dictionary<string, object>
            {
                { "Amount", 6000 },
                { "Category", "Travel" }
            };

            var events = await _unitOfWork.Events.ListByCorrelationIdAsync(instanceId);
            var lastEvent = events.OrderByDescending(e => e.Timestamp).FirstOrDefault();

            if (lastEvent != null && lastEvent.Metadata.ContainsKey("Payload"))
            {
                try
                {
                    var json = lastEvent.Metadata["Payload"]?.ToString();
                    var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json ?? "");
                    if (dict != null) payload = dict;
                }
                catch
                {
                    /* Ignore parsing error */
                }
            }

            var context = new AgentContext(
                instance.TenantId,
                payload,
                instance.CurrentStepId,
                new List<FlowOS.Events.Abstractions.IEvent>(),
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

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = new List<ToolContent>
        {
            new ToolContent { Type = "text", Text = message }
        }
    };
}
