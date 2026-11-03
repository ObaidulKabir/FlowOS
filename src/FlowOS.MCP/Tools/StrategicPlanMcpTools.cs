using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

/// <summary>
/// ADR-005 Phase 3: MCP tools for the External Orchestrator Agent pattern.
///
/// Two tools are provided:
///   - get_strategic_plan_status  — reads a StrategicPlan WorkflowInstance snapshot
///   - get_team_execution_outcome — reads the enriched outcome of a TeamExecution step
///
/// Three inviolable rules (ADR-005):
///   1. FlowOS never sequences across teams — the orchestrator does.
///   2. The orchestrator uses MCP tools only; it never calls internal APIs.
///   3. The StrategicPlan WorkflowClass is the single source of truth.
/// </summary>
public sealed class StrategicPlanMcpTools
{
    private readonly IUnitOfWork _unitOfWork;

    public StrategicPlanMcpTools(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // ── get_strategic_plan_status ────────────────────────────────────────────

    /// <summary>
    /// Returns a lightweight snapshot of a StrategicPlan WorkflowInstance:
    /// current step, status, role assignments, and timestamps.
    ///
    /// The External Orchestrator Agent calls this after each
    /// AgentTaskCompleted change-feed event to decide which team to start next.
    /// </summary>
    public async Task<CallToolResult> GetStrategicPlanStatus(JObject args)
    {
        try
        {
            if (!TryParseGuid(args, "workflowInstanceId", out var workflowInstanceId))
                return McpToolResults.Fail("MCP-ARG-001", "workflowInstanceId is required and must be a valid UUID.");

            var tenantId = ResolveTenant(args);
            if (tenantId == Guid.Empty)
                return McpToolResults.Fail("MCP-ARG-001", "tenantId is required when not using HTTP authenticated context.");

            var instance = await _unitOfWork.WorkflowInstances
                .GetByIdAsNoTrackingAsync(workflowInstanceId, tenantId, CancellationToken.None);

            if (instance is null)
                return McpToolResults.Fail("MCP-NOTFOUND-001",
                    $"WorkflowInstance '{workflowInstanceId}' was not found for this tenant.");

            return McpToolResults.Success(new
            {
                workflowInstanceId = instance.Id,
                tenantId = instance.TenantId,
                workflowClassId = instance.WorkflowClassId,
                workflowDefinitionId = instance.WorkflowDefinitionId,
                status = instance.Status.ToString(),
                currentStepId = instance.CurrentStepId,
                activeStepIds = instance.ActiveStepIds,
                completedParallelStepIds = instance.CompletedParallelStepIds,
                roleAssignments = instance.RoleAssignments,
                createdAt = instance.CreatedAt,
                completedAt = instance.CompletedAt,
                parentWorkflowInstanceId = instance.ParentWorkflowInstanceId,
                parentStepId = instance.ParentStepId,
                externalReference = instance.ExternalReference,
                priority = instance.Priority
            });
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL",
                $"Failed to get strategic plan status: {ex.Message}");
        }
    }

    // ── get_team_execution_outcome ───────────────────────────────────────────

    /// <summary>
    /// Returns the outcome of a TeamExecution for a specific workflow step,
    /// including the committed event, confidence score, and actor agent ID.
    ///
    /// The External Orchestrator Agent calls this to read step outcomes after
    /// observing an AgentTaskCompleted change-feed event, then decides which
    /// team or workflow event to trigger next.
    /// </summary>
    public async Task<CallToolResult> GetTeamExecutionOutcome(JObject args)
    {
        try
        {
            var tenantId = ResolveTenant(args);
            if (tenantId == Guid.Empty)
                return McpToolResults.Fail("MCP-ARG-001", "tenantId is required when not using HTTP authenticated context.");

            // Allow lookup by teamExecutionId OR by (workflowInstanceId + stepId).
            if (TryParseGuid(args, "teamExecutionId", out var teamExecutionId))
            {
                var execution = await _unitOfWork.TeamExecutions
                    .GetByIdAsync(tenantId, teamExecutionId, CancellationToken.None);

                if (execution is null)
                    return McpToolResults.Fail("MCP-NOTFOUND-001",
                        $"TeamExecution '{teamExecutionId}' was not found for this tenant.");

                return McpToolResults.Success(BuildOutcomePayload(execution));
            }

            if (!TryParseGuid(args, "workflowInstanceId", out var workflowInstanceId))
                return McpToolResults.Fail("MCP-ARG-001",
                    "Either teamExecutionId or (workflowInstanceId + stepId) is required.");

            var stepId = args["stepId"]?.ToString();
            if (string.IsNullOrWhiteSpace(stepId))
                return McpToolResults.Fail("MCP-ARG-001",
                    "stepId is required when workflowInstanceId is provided.");

            var byStep = await _unitOfWork.TeamExecutions
                .GetActiveForStepAsync(tenantId, workflowInstanceId, stepId, CancellationToken.None);

            if (byStep is null)
                return McpToolResults.Fail("MCP-NOTFOUND-001",
                    $"No TeamExecution found for workflowInstanceId '{workflowInstanceId}' and stepId '{stepId}'.");

            return McpToolResults.Success(BuildOutcomePayload(byStep));
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL",
                $"Failed to get team execution outcome: {ex.Message}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static object BuildOutcomePayload(FlowOS.Domain.Entities.Orchestration.TeamExecution ex) => new
    {
        teamExecutionId = ex.Id,
        tenantId = ex.TenantId,
        agentTeamId = ex.AgentTeamId,
        workflowInstanceId = ex.WorkflowInstanceId,
        workflowStepId = ex.WorkflowStepId,
        status = ex.Status.ToString(),
        // ADR-005 Phase 3 outcome fields:
        committedEvent = ex.CommittedEvent,
        confidence = ex.Confidence,
        actorAgentId = ex.ActorAgentId,
        slotAssignments = ex.SlotAssignments,
        objective = ex.Objective,
        createdAtUtc = ex.CreatedAtUtc,
        startedAtUtc = ex.StartedAtUtc,
        completedAtUtc = ex.CompletedAtUtc,
        failureReason = ex.FailureReason
    };

    private static bool TryParseGuid(JObject args, string key, out Guid result)
    {
        result = Guid.Empty;
        return args[key] != null && Guid.TryParse(args[key]?.ToString(), out result) && result != Guid.Empty;
    }

    private static Guid ResolveTenant(JObject args)
    {
        if (TryParseGuid(args, "tenantId", out var from_args))
            return from_args;
        return McpRequestContext.TenantId;
    }
}
