using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class AgentTools
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAgentTaskRunner _agentTaskRunner;
    private readonly IPluginBindingRegistryService? _pluginBindings;
    private readonly IFlowOsHostedLlmRuntime? _hosted;

    public AgentTools(
        IUnitOfWork unitOfWork,
        IAgentTaskRunner agentTaskRunner,
        IPluginBindingRegistryService? pluginBindings = null,
        IFlowOsHostedLlmRuntime? hosted = null)
    {
        _unitOfWork = unitOfWork;
        _agentTaskRunner = agentTaskRunner;
        _pluginBindings = pluginBindings;
        _hosted = hosted;
    }

    public async Task<CallToolResult> ListAvailableAgents(JObject args)
    {
        var hosted = _hosted?.PublicSettings;
        var agents = new List<object>
        {
            new
            {
                id = "flowos-hosted",
                name = "FlowOS Hosted OpenAI",
                kind = "flowos-hosted",
                description = "Paid-plan default. FlowOS calls OpenAI with a platform key. No tenant API key. Capped by MaxCompletionsPerDay.",
                hasApiKey = hosted?.HasApiKey ?? false,
                model = hosted?.Model ?? "gpt-4o-mini",
                isEnabled = hosted?.Enabled ?? false,
                capabilities = new[] { "legal nextSteps only" }
            },
            new
            {
                id = "RiskAnalysisAgent",
                name = "Risk Analyzer",
                kind = "fixture",
                description = "No-key fixture (flowos-risk). Used only when the step is explicitly flowos-risk or hosted OpenAI is not configured.",
                hasApiKey = false,
                capabilities = new[] { "legal nextSteps only" }
            }
        };

        if (_pluginBindings != null)
        {
            try
            {
                var tenantId = McpTenantResolver.ResolveRequired(args);
                var bindings = await _pluginBindings.ListAsync(tenantId, PluginBindingTypes.Agent);
                foreach (var binding in bindings)
                {
                    var settings = binding.Configuration as AgentProviderPublicSettings;
                    agents.Add(new
                    {
                        id = binding.SourceName,
                        name = binding.SourceName,
                        kind = binding.ProviderName,
                        description = "Tenant AI Context provider. Point the waiting step at this alias with agentProvider, then call run_agent_task.",
                        hasApiKey = settings?.HasApiKey ?? false,
                        isEnabled = binding.IsEnabled,
                        model = settings?.Model,
                        capabilities = new[] { "legal nextSteps only" }
                    });
                }
            }
            catch (McpToolException)
            {
                // Fixture-only when the caller has no tenant yet.
            }
        }

        return McpToolResults.Success(new { agents });
    }

    public async Task<CallToolResult> SuggestAgentAction(JObject args)
    {
        try
        {
            var instanceIdStr = args["workflowInstanceId"]?.ToString();
            var agentId = args["agentId"]?.ToString();
            if (string.IsNullOrWhiteSpace(agentId))
                agentId = "RiskAnalysisAgent";

            if (string.IsNullOrEmpty(instanceIdStr) || !Guid.TryParse(instanceIdStr, out var instanceId))
                return McpToolResults.Fail("MCP-ARG-002", "workflowInstanceId must be a valid UUID.");

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
            {
                var reason = run.SkipReason ?? "Agent task was not run.";
                return McpToolResults.Fail(HostedFailureCode(reason), reason);
            }

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

    private static string HostedFailureCode(string reason)
    {
        if (reason.StartsWith(TenantEntitlementPolicy.PlanRequiredCode, StringComparison.Ordinal))
            return TenantEntitlementPolicy.PlanRequiredCode;
        if (reason.StartsWith(FlowOsHostedLlmCodes.Quota, StringComparison.Ordinal))
            return FlowOsHostedLlmCodes.Quota;
        if (reason.StartsWith(FlowOsHostedLlmCodes.Unavailable, StringComparison.Ordinal))
            return FlowOsHostedLlmCodes.Unavailable;
        return "MCP-NOTFOUND-001";
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
