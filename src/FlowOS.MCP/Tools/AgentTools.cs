using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class AgentTools
{
    private static readonly TimeSpan SynchronousWaitTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SynchronousPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SynchronousClaimDuration = TimeSpan.FromMinutes(2);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAgentTaskCoordinator _agentTaskCoordinator;
    private readonly IPluginBindingRegistryService? _pluginBindings;
    private readonly IFlowOsHostedLlmRuntime? _hosted;

    public AgentTools(
        IUnitOfWork unitOfWork,
        IAgentTaskCoordinator agentTaskCoordinator,
        IPluginBindingRegistryService? pluginBindings = null,
        IFlowOsHostedLlmRuntime? hosted = null)
    {
        _unitOfWork = unitOfWork;
        _agentTaskCoordinator = agentTaskCoordinator;
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
                actorId = AgentProviderKinds.FlowosRisk,
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

            if (string.IsNullOrEmpty(instanceIdStr) || !Guid.TryParse(instanceIdStr, out var instanceId))
                return McpToolResults.Fail("MCP-ARG-002", "workflowInstanceId must be a valid UUID.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var instance = await _unitOfWork.WorkflowInstances
                .GetByIdAsNoTrackingAsync(instanceId, tenantId);
            if (instance == null)
                return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowInstance not found.");

            var request = new AgentTaskEnqueueRequest(
                tenantId,
                instanceId,
                instance.CurrentStepId,
                string.IsNullOrWhiteSpace(agentId) ? "RiskAnalysisAgent" : agentId.Trim(),
                args["objective"]?.ToString(),
                AllowAutoCommit: false,
                RequireAgentActor: false,
                AgentTaskSource.McpRequest,
                ActiveKey: CreateSuggestionActiveKey(
                    tenantId,
                    instanceId,
                    instance.CurrentStepId));
            var coordinated = await _agentTaskCoordinator.EnqueueAndExecuteAsync(
                request,
                $"mcp-suggest:{Guid.NewGuid():N}",
                SynchronousWaitTimeout,
                SynchronousPollInterval,
                SynchronousClaimDuration,
                CancellationToken.None);
            var run = coordinated.RunResult;

            if (run == null || !run.Ran)
            {
                var reason = run?.SkipReason
                    ?? coordinated.Message
                    ?? "Agent suggestion failed.";
                var code = string.Equals(reason, "Workflow instance was not found.", StringComparison.Ordinal)
                    ? "MCP-NOTFOUND-001"
                    : string.Equals(reason, $"Agent '{agentId}' was not found.", StringComparison.Ordinal)
                        ? "MCP-NOTFOUND-001"
                        : "MCP-INTERNAL";
                return McpToolResults.Fail(code, reason, new
                {
                    jobId = coordinated.JobId,
                    executionId = run?.ExecutionId
                });
            }

            return McpToolResults.Success(new
            {
                jobId = coordinated.JobId,
                executionId = run.ExecutionId,
                autoCommitted = false,
                parkReason = run.ParkReason,
                agent = AgentDescriptor(run),
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
            var agentId = args["agentId"]?.ToString();

            if (string.IsNullOrEmpty(instanceIdStr) || !Guid.TryParse(instanceIdStr, out var instanceId))
                return McpToolResults.Fail("MCP-ARG-002", "workflowInstanceId must be a valid UUID.");

            var tenantId = McpTenantResolver.ResolveRequired(args);
            var instance = await _unitOfWork.WorkflowInstances.GetByIdAsNoTrackingAsync(instanceId, tenantId);
            if (instance == null)
                return McpToolResults.Fail("MCP-NOTFOUND-001", "WorkflowInstance not found.");

            var coordinated = await _agentTaskCoordinator.EnqueueAndExecuteAsync(
                new AgentTaskEnqueueRequest(
                    tenantId,
                    instanceId,
                    instance.CurrentStepId,
                    string.IsNullOrWhiteSpace(agentId) ? "RiskAnalysisAgent" : agentId.Trim(),
                    "Decide the next legal workflow event.",
                    AllowAutoCommit: true,
                    RequireAgentActor: true,
                    AgentTaskSource.McpRequest),
                $"mcp-run:{Guid.NewGuid():N}",
                SynchronousWaitTimeout,
                SynchronousPollInterval,
                SynchronousClaimDuration,
                CancellationToken.None);
            var run = coordinated.RunResult;

            if (run == null || !run.Ran)
            {
                var reason = run?.SkipReason
                    ?? coordinated.Message
                    ?? "Agent task was not run.";
                return McpToolResults.Fail(HostedFailureCode(reason), reason, new
                {
                    jobId = coordinated.JobId,
                    executionId = run?.ExecutionId
                });
            }

            return McpToolResults.Success(new
            {
                jobId = coordinated.JobId,
                executionId = run.ExecutionId,
                autoCommitted = run.AutoCommitted,
                parkReason = run.ParkReason,
                agent = AgentDescriptor(run),
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

    private static string CreateSuggestionActiveKey(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId) =>
        AgentTaskJob.CreateActiveKey(tenantId, workflowInstanceId, stepId)
            .Replace("agent-task:", "agent-suggest:", StringComparison.Ordinal);

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

    private static object AgentDescriptor(AgentTaskRunResult run) =>
        new
        {
            id = run.AgentId,
            runtime = run.RuntimeIdentifier,
            providerAlias = run.ProviderAlias,
            providerName = run.ProviderName,
            model = run.Model
        };

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
