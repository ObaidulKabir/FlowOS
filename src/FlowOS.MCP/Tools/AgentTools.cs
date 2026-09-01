using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
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

        return Task.FromResult(McpToolResults.Success(new { agents }));
    }

    public async Task<CallToolResult> SuggestAgentAction(JObject args)
    {
        try
        {
            var instanceIdStr = args["workflowInstanceId"]?.ToString();
            var agentId = args["agentId"]?.ToString();

            if (string.IsNullOrEmpty(instanceIdStr) || !Guid.TryParse(instanceIdStr, out var instanceId))
                return McpToolResults.Fail("MCP-ARG-002", "workflowInstanceId must be a valid UUID.");

            if (string.IsNullOrEmpty(agentId))
                return McpToolResults.Fail("MCP-ARG-001", "agentId is required.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var instance = await _unitOfWork.WorkflowInstances.GetByIdAsNoTrackingAsync(instanceId, tenantId);
            if (instance == null) return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowInstance not found.");

            IWorkflowAgent? agent = agentId == "RiskAnalysisAgent" ? new RiskAnalysisAgent() : null;
            if (agent == null) return McpToolResults.Fail("MCP-NOTFOUND-001", $"Agent '{agentId}' not found.");

            // ---- New: optional objective ----
            var objective = args["objective"]?.ToString() ?? "Analyze workflow instance";

            // ---- New: fetch and order events ----
            var events = await _unitOfWork.Events.ListByCorrelationIdAsync(instanceId);
            var orderedEvents = events.OrderBy(e => e.Timestamp).ToList();

            // ---- New: aggregate payload from all events (last-write-wins) ----
            var payload = new Dictionary<string, object>();
            foreach (var ev in orderedEvents)
            {
                if (!ev.Metadata.ContainsKey("Payload")) continue;
                try
                {
                    var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(ev.Metadata["Payload"]?.ToString() ?? "");
                    if (dict != null)
                    {
                        foreach (var kv in dict)
                            payload[kv.Key] = kv.Value; // overwrite with later values
                    }
                }
                catch { /* ignore malformed payloads */ }
            }

            if (payload.Count == 0)
            {
                return McpToolResults.Fail("MCP-NODATA-001", "No payload data found in the event history for this workflow instance.");
            }

            var context = new AgentContext(
                instance.TenantId,
                payload,
                instance.CurrentStepId,
                orderedEvents,
                objective
            );

            var result = await agent.ExecuteAsync(context);

            return McpToolResults.Success(result);
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Agent execution failed.");
        }
    }
}
