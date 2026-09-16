using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class AgentTools
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAgentTaskRunner _agentTaskRunner;

    public AgentTools(IUnitOfWork unitOfWork, IAgentTaskRunner agentTaskRunner)
    {
        _unitOfWork = unitOfWork;
        _agentTaskRunner = agentTaskRunner;
    }

    public Task<CallToolResult> ListAvailableAgents(JObject args)
    {
        var agents = new List<object>
        {
            new
            {
                id = "RiskAnalysisAgent",
                name = "Risk Analyzer",
                description = "Analyzes expense data for high-value risks and fraud patterns using the DecisionPacket (template guideline, binding policy, legal nextSteps).",
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
            var run = await _agentTaskRunner.SuggestAsync(
                tenantId,
                instanceId,
                agentId,
                args["objective"]?.ToString(),
                CancellationToken.None);

            if (!run.Ran)
            {
                var code = string.Equals(run.SkipReason, "Workflow instance was not found.", StringComparison.Ordinal)
                    ? "MCP-NOTFOUND-001"
                    : string.Equals(run.SkipReason, $"Agent '{agentId}' was not found.", StringComparison.Ordinal)
                        ? "MCP-NOTFOUND-001"
                        : "MCP-INTERNAL";
                return McpToolResults.Fail(code, run.SkipReason ?? "Agent suggestion failed.");
            }

            return McpToolResults.Success(new
            {
                autoCommitted = false,
                parkReason = run.ParkReason,
                packet = SummarizePacket(run.Packet),
                result = run.AgentResult
            });
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

    public async Task<CallToolResult> RunAgentTask(JObject args)
    {
        try
        {
            var instanceIdStr = args["workflowInstanceId"]?.ToString();
            var agentId = args["agentId"]?.ToString() ?? "RiskAnalysisAgent";

            if (string.IsNullOrEmpty(instanceIdStr) || !Guid.TryParse(instanceIdStr, out var instanceId))
                return McpToolResults.Fail("MCP-ARG-002", "workflowInstanceId must be a valid UUID.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var instance = await _unitOfWork.WorkflowInstances.GetByIdAsNoTrackingAsync(instanceId, tenantId);
            if (instance == null)
                return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowInstance not found.");

            var run = await _agentTaskRunner.TryRunForCurrentStepAsync(
                tenantId,
                instanceId,
                agentId,
                CancellationToken.None);

            if (!run.Ran)
                return McpToolResults.Fail("MCP-NOTFOUND-001", run.SkipReason ?? "Agent task was not run.");

            return McpToolResults.Success(new
            {
                autoCommitted = run.AutoCommitted,
                parkReason = run.ParkReason,
                packet = SummarizePacket(run.Packet),
                result = run.AgentResult
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Agent task failed.");
        }
    }

    private static object? SummarizePacket(DecisionPacket? packet)
    {
        if (packet == null) return null;
            return new
            {
                packet.CurrentStepId,
                packet.CurrentState,
                packet.Actor,
                prompt = new
                {
                    packet.Prompt.Alias,
                    packet.Prompt.Title,
                    packet.Prompt.System,
                    packet.Prompt.Instructions,
                    packet.Prompt.TemplateGuideline,
                    packet.Prompt.PolicyGuideline,
                    packet.Prompt.Objective
                },
                data = new
                {
                    canonicalKeys = packet.Data.CanonicalContext.Keys,
                    packet.Data.SlaReminders,
                    packet.Data.TimeoutEvent
                },
                provider = packet.Provider == null
                    ? null
                    : new
                    {
                        packet.Provider.Alias,
                        packet.Provider.ProviderName,
                        packet.Provider.Model,
                        packet.Provider.Endpoint,
                        packet.Provider.HasApiKey
                    },
                tools = packet.DeclaredTools.Select(tool => new
                {
                    tool.Name,
                    tool.Kind,
                    tool.Provider,
                    tool.Capability,
                    tool.SideEffect,
                    tool.Prefetch,
                    tool.Description
                }),
                toolResults = packet.ToolResults,
                packet.LegalNextStepEvents,
                packet.LegalStateMachineEvents,
                autoCommit = packet.AutoCommit == null
                    ? null
                    : new { packet.AutoCommit.MinConfidence, packet.AutoCommit.AllowedEvents }
            };
    }
}
