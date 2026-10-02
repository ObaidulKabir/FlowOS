using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities.ExternalAI;
using FlowOS.Domain.Enums;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class ExternalAgentChangeFeedMcpTools
{
    private readonly IExternalAgentChangeStore _changeStore;
    private readonly IExternalAgentPlanStore? _planStore;

    public ExternalAgentChangeFeedMcpTools(
        IExternalAgentChangeStore changeStore,
        IExternalAgentPlanStore? planStore = null)
    {
        _changeStore = changeStore;
        _planStore = planStore;
    }

    public async Task<CallToolResult> PollExternalAgentChanges(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            var limit = args["limit"]?.Value<int>() ?? 10;
            if (limit < 1) limit = 1;
            if (limit > 50) limit = 50;

            var peekOnly = args["peekOnly"]?.Value<bool>() ?? false;

            var ttlSeconds = args["ttlSeconds"]?.Value<int>() ?? 120;
            if (ttlSeconds < 60) ttlSeconds = 60;
            if (ttlSeconds > 3600) ttlSeconds = 3600;

            IReadOnlyList<ExternalAgentChangeRecord> changes;

            if (peekOnly)
            {
                changes = await _changeStore.ListPendingAsync(tenantId, limit);
            }
            else
            {
                var callerId = $"mcp:{tenantId}:{Guid.NewGuid():N}";
                var ttl = TimeSpan.FromSeconds(ttlSeconds);
                changes = await _changeStore.LeaseNextAsync(tenantId, callerId, ttl, limit);
            }

            return McpToolResults.Success(new
            {
                tenantId,
                peekOnly,
                count = changes.Count,
                changes = changes.Select(c => MapChange(c)).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to poll external agent changes: {ex.Message}");
        }
    }

    public async Task<CallToolResult> AckExternalAgentChange(JObject args)
    {
        try
        {
            if (args["changeId"] == null || !Guid.TryParse(args["changeId"]?.ToString(), out var changeId) || changeId == Guid.Empty)
            {
                return McpToolResults.Fail("MCP-ARG-001", "changeId is required and must be a valid UUID.");
            }

            var resultStr = args["result"]?.ToString();
            if (string.IsNullOrWhiteSpace(resultStr))
            {
                return McpToolResults.Fail("MCP-ARG-001", "result is required ('succeeded' or 'failed').");
            }

            bool succeeded;
            if (string.Equals(resultStr, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                succeeded = true;
            }
            else if (string.Equals(resultStr, "failed", StringComparison.OrdinalIgnoreCase))
            {
                succeeded = false;
            }
            else
            {
                return McpToolResults.Fail("MCP-ARG-001", "result must be 'succeeded' or 'failed'.");
            }

            var error = args["error"]?.ToString();

            await _changeStore.AckAsync(changeId, succeeded, string.IsNullOrWhiteSpace(error) ? null : error);

            var change = await _changeStore.GetByIdAsync(changeId);
            return McpToolResults.Success(new
            {
                changeId,
                acknowledged = true,
                result = succeeded ? "succeeded" : "failed",
                finalStatus = change?.Status.ToString() ?? "Processed"
            });
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to acknowledge external agent change: {ex.Message}");
        }
    }

    public async Task<CallToolResult> RenewChangeLease(JObject args)
    {
        try
        {
            if (args["changeId"] == null || !Guid.TryParse(args["changeId"]?.ToString(), out var changeId) || changeId == Guid.Empty)
            {
                return McpToolResults.Fail("MCP-ARG-001", "changeId is required and must be a valid UUID.");
            }

            var ttlSeconds = args["ttlSeconds"]?.Value<int>() ?? 120;
            if (ttlSeconds < 60) ttlSeconds = 60;
            if (ttlSeconds > 3600) ttlSeconds = 3600;

            var change = await _changeStore.GetByIdAsync(changeId);
            if (change == null)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Change record '{changeId}' not found.");
            }

            if (change.Status != ExternalAgentChangeStatus.Leased)
            {
                return McpToolResults.Fail("MCP-STATE-001", $"Cannot renew lease for change in status {change.Status}. Only Leased changes can be renewed.");
            }

            var agentId = change.LeasedByAgent;
            if (string.IsNullOrWhiteSpace(agentId))
            {
                return McpToolResults.Fail("MCP-STATE-001", "Change is marked Leased but has no LeasedByAgent identifier.");
            }

            var ttl = TimeSpan.FromSeconds(ttlSeconds);
            await _changeStore.RenewLeaseAsync(changeId, agentId, ttl);

            var updated = await _changeStore.GetByIdAsync(changeId);
            return McpToolResults.Success(new
            {
                changeId,
                renewed = true,
                ttlSeconds,
                leasedUntilUtc = updated?.LeasedUntilUtc
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to renew change lease: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ListExternalAgentChanges(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            var limit = args["limit"]?.Value<int>() ?? 20;
            if (limit < 1) limit = 1;
            if (limit > 100) limit = 100;

            var statusFilter = args["status"]?.ToString()?.Trim();
            ExternalAgentChangeStatus? parsedStatus = null;
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                if (Enum.TryParse<ExternalAgentChangeStatus>(statusFilter, true, out var s))
                {
                    parsedStatus = s;
                }
            }

            var resultList = await _changeStore.ListAsync(tenantId, parsedStatus, limit);

            return McpToolResults.Success(new
            {
                tenantId,
                statusFilter = statusFilter ?? (object?)null,
                filteredCount = resultList.Count,
                changes = resultList.Select(c => MapChange(c)).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to list external agent changes: {ex.Message}");
        }
    }

    public async Task<CallToolResult> GetExternalAgentChange(JObject args)
    {
        try
        {
            if (args["changeId"] == null || !Guid.TryParse(args["changeId"]?.ToString(), out var changeId) || changeId == Guid.Empty)
            {
                return McpToolResults.Fail("MCP-ARG-001", "changeId is required and must be a valid UUID.");
            }

            var change = await _changeStore.GetByIdAsync(changeId);
            if (change == null)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Change record '{changeId}' not found.");
            }

            object? planInfo = null;
            if (_planStore != null)
            {
                try
                {
                    var plan = await _planStore.GetLatestByChangeAsync(change.Id);
                    if (plan != null)
                    {
                        var planSteps = await _planStore.GetPlanStepsAsync(plan.Id);
                        planInfo = new
                        {
                            planId = plan.Id,
                            changeId = plan.ChangeId,
                            planStepCount = planSteps.Count,
                            planSteps = planSteps.Select(p => new
                            {
                                stepRecordId = p.Id,
                                stepId = p.StepId,
                                stepIndex = p.StepIndex,
                                toolName = p.ToolName,
                                status = p.Status.ToString(),
                                description = p.Description
                            }).ToList()
                        };
                    }
                }
                catch
                {
                    planInfo = null;
                }
            }

            var result = MapChange(change);
            if (planInfo != null)
            {
                result["associatedPlan"] = JToken.FromObject(planInfo);
            }

            return McpToolResults.Success(result);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to get external agent change: {ex.Message}");
        }
    }

    private static JObject MapChange(ExternalAgentChangeRecord c)
    {
        var obj = new JObject
        {
            ["id"] = c.Id,
            ["tenantId"] = c.TenantId,
            ["sourceOutboxMessageId"] = c.SourceOutboxMessageId,
            ["eventType"] = c.EventType,
            ["status"] = c.Status.ToString(),
            ["leasedByAgent"] = string.IsNullOrWhiteSpace(c.LeasedByAgent) ? null : c.LeasedByAgent,
            ["leasedUntilUtc"] = c.LeasedUntilUtc,
            ["attemptCount"] = c.AttemptCount,
            ["maxAttempts"] = c.MaxAttempts,
            ["nextRetryUtc"] = c.NextRetryUtc,
            ["lastError"] = string.IsNullOrWhiteSpace(c.LastError) ? null : c.LastError,
            ["createdAtUtc"] = c.CreatedAtUtc,
            ["processedAtUtc"] = c.ProcessedAtUtc,
            ["payloadJson"] = ParsePayload(c.PayloadJson)
        };
        return obj;
    }

    private static JToken? ParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        try
        {
            return JToken.Parse(payloadJson);
        }
        catch
        {
            return payloadJson;
        }
    }
}
