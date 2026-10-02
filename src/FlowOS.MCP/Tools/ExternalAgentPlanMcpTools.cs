using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public sealed class ExternalAgentPlanMcpTools
{
    private readonly IExternalAIAgentPlanner _planner;
    private readonly IExternalAIAgentExecutor _executor;
    private readonly IExternalAgentPlanStore _planStore;

    public ExternalAgentPlanMcpTools(
        IExternalAIAgentPlanner planner,
        IExternalAIAgentExecutor executor,
        IExternalAgentPlanStore planStore)
    {
        _planner = planner;
        _executor = executor;
        _planStore = planStore;
    }

    public async Task<CallToolResult> PlanForChange(JObject args)
    {
        try
        {
            if (args["changeId"] == null || !Guid.TryParse(args["changeId"]?.ToString(), out var changeId) || changeId == Guid.Empty)
                return McpToolResults.Fail("MCP-ARG-001", "changeId is required and must be a valid UUID.");

            var tenantId = ResolveOptionalTenant(args);
            var result = await _planner.PlanAsync(
                changeId,
                tenantId,
                args["agentProfileId"]?.ToString(),
                args["objective"]?.ToString(),
                CancellationToken.None);

            return McpToolResults.Success(new
            {
                planId = result.PlanId,
                changeId = result.ChangeId,
                tenantId = result.TenantId,
                agentProfileId = result.AgentProfileId,
                validationDropped = result.ValidationDropped,
                steps = result.Steps.Select(step => new
                {
                    step.StepId,
                    step.StepIndex,
                    step.ToolName,
                    toolArgs = JObject.Parse(step.ToolArgsJson),
                    step.Description,
                    dependsOn = step.DependsOn
                }).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return IsTenantError(ex)
                ? McpToolResults.Fail("MCP-TENANT-001", ex.Message)
                : McpToolResults.Fail("MCP-INTERNAL", $"Failed to plan for change: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ExecutePlan(JObject args)
    {
        try
        {
            if (args["planId"] == null || !Guid.TryParse(args["planId"]?.ToString(), out var planId) || planId == Guid.Empty)
                return McpToolResults.Fail("MCP-ARG-001", "planId is required and must be a valid UUID.");

            var result = await _executor.ExecutePlanAsync(planId, ResolveOptionalTenant(args), CancellationToken.None);
            return McpToolResults.Success(new
            {
                result.PlanId,
                result.TenantId,
                result.Success,
                stepCount = result.StepResults.Count,
                stepResults = result.StepResults
            });
        }
        catch (Exception ex)
        {
            return IsTenantError(ex)
                ? McpToolResults.Fail("MCP-TENANT-001", ex.Message)
                : McpToolResults.Fail("MCP-INTERNAL", $"Failed to execute plan: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ResumePlan(JObject args)
    {
        try
        {
            if (args["planId"] == null || !Guid.TryParse(args["planId"]?.ToString(), out var planId) || planId == Guid.Empty)
                return McpToolResults.Fail("MCP-ARG-001", "planId is required and must be a valid UUID.");

            var result = await _executor.ResumePlanAsync(
                planId,
                args["fromStepId"]?.ToString(),
                ResolveOptionalTenant(args),
                CancellationToken.None);

            return McpToolResults.Success(new
            {
                result.PlanId,
                result.TenantId,
                result.Success,
                stepCount = result.StepResults.Count,
                stepResults = result.StepResults
            });
        }
        catch (Exception ex)
        {
            return IsTenantError(ex)
                ? McpToolResults.Fail("MCP-TENANT-001", ex.Message)
                : McpToolResults.Fail("MCP-INTERNAL", $"Failed to resume plan: {ex.Message}");
        }
    }

    public async Task<CallToolResult> GetExternalAgentPlan(JObject args)
    {
        try
        {
            if (args["planId"] == null || !Guid.TryParse(args["planId"]?.ToString(), out var planId) || planId == Guid.Empty)
                return McpToolResults.Fail("MCP-ARG-001", "planId is required and must be a valid UUID.");

            var plan = await _planStore.GetPlanAsync(planId, CancellationToken.None);
            if (plan == null)
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Plan '{planId}' was not found.");

            var tenantId = ResolveOptionalTenant(args);
            if (tenantId.HasValue && tenantId.Value != plan.TenantId)
                return McpToolResults.Fail("MCP-TENANT-001", "Requested tenant does not own the plan.");

            var steps = await _planStore.GetPlanStepsAsync(planId, CancellationToken.None);
            return McpToolResults.Success(new
            {
                plan.Id,
                plan.ChangeId,
                plan.TenantId,
                plan.AgentProfileId,
                plan.CreatedAtUtc,
                plan.PlanVersion,
                steps = steps.Select(step => new
                {
                    step.Id,
                    step.StepId,
                    step.StepIndex,
                    step.ToolName,
                    step.Status,
                    step.StartedAtUtc,
                    step.FinishedAtUtc,
                    step.ErrorMessage,
                    step.ResultSnapshotJson
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to get external agent plan: {ex.Message}");
        }
    }

    private static Guid? ResolveOptionalTenant(JObject args)
    {
        if (args["tenantId"] != null && Guid.TryParse(args["tenantId"]?.ToString(), out var tenantId))
            return tenantId;

        if (McpRequestContext.TenantId != Guid.Empty)
            return McpRequestContext.TenantId;

        return null;
    }

    private static bool IsTenantError(Exception ex) =>
        ex.Message.Contains("does not own", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Requested tenant", StringComparison.OrdinalIgnoreCase);
}
