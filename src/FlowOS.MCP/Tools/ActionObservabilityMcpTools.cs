using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class ActionObservabilityMcpTools
{
    private readonly IWorkflowActionHistoryService _actionHistoryService;

    public ActionObservabilityMcpTools(IWorkflowActionHistoryService actionHistoryService)
    {
        _actionHistoryService = actionHistoryService;
    }

    public async Task<CallToolResult> GetInstanceActionHistory(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            var instanceIdStr = args["workflowInstanceId"]?.ToString() ?? args["instanceId"]?.ToString();
            if (string.IsNullOrWhiteSpace(instanceIdStr) || !Guid.TryParse(instanceIdStr, out var workflowInstanceId))
            {
                return McpToolResults.Fail("MCP-ARG-001", "workflowInstanceId is required and must be a valid UUID.");
            }

            var stepId = args["stepId"]?.ToString()?.Trim();
            var actionType = args["actionType"]?.ToString()?.Trim();
            var status = args["status"]?.ToString()?.Trim();
            var limit = args["limit"]?.Value<int>() ?? 50;

            var actions = await _actionHistoryService.GetActionHistoryAsync(
                tenantId: tenantId,
                workflowInstanceId: workflowInstanceId,
                stepId: string.IsNullOrEmpty(stepId) ? null : stepId,
                actionType: string.IsNullOrEmpty(actionType) ? null : actionType,
                status: string.IsNullOrEmpty(status) ? null : status,
                page: 1,
                pageSize: limit,
                ct: CancellationToken.None
            );

            return McpToolResults.Success(new
            {
                workflowInstanceId = workflowInstanceId,
                totalActions = actions.Count,
                actions = actions.Select(a => new
                {
                    id = a.Id,
                    stepId = a.StepId,
                    triggerPhase = a.TriggerPhase,
                    actionType = a.ActionType,
                    target = a.Target,
                    status = a.Status,
                    executedAtUtc = a.ExecutedAtUtc,
                    durationMs = a.DurationMs,
                    httpStatusCode = a.HttpStatusCode,
                    requestPayloadSnippet = a.RequestPayloadSnippet,
                    responseSnippet = a.ResponseSnippet,
                    errorMessage = a.ErrorMessage,
                    attemptNumber = a.AttemptNumber,
                    outboxMessageId = a.OutboxMessageId
                })
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to retrieve action execution history: {ex.Message}");
        }
    }
}
