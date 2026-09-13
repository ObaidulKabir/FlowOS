using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public class DeadLetterMcpTools
{
    private readonly IDeadLetterService _deadLetterService;

    public DeadLetterMcpTools(IDeadLetterService deadLetterService)
    {
        _deadLetterService = deadLetterService;
    }

    public async Task<CallToolResult> ListDeadLetters(JObject args)
    {
        try
        {
            var tenantId = ResolveOptionalTenant(args);
            var type = args["type"]?.ToString()?.Trim();
            var limit = args["limit"]?.Value<int>() ?? 50;
            if (limit <= 0) limit = 50;

            var items = await _deadLetterService.GetDeadLettersAsync(tenantId, type, 1, limit);

            return McpToolResults.Success(new
            {
                totalCount = items.Count,
                deadLetters = items.Select(d => new
                {
                    id = d.Id,
                    tenantId = d.TenantId,
                    type = d.Type,
                    occurredOnUtc = d.OccurredOnUtc,
                    retryCount = d.RetryCount,
                    maxRetries = d.MaxRetries,
                    error = d.Error,
                    actionType = d.ActionType,
                    targetUrl = d.TargetUrl,
                    httpMethod = d.HttpMethod,
                    stepId = d.StepId,
                    payload = TryParsePayload(d.Payload)
                }).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to list dead letters: {ex.Message}");
        }
    }

    public async Task<CallToolResult> RetryDeadLetter(JObject args)
    {
        try
        {
            var tenantId = ResolveOptionalTenant(args);
            var isAll = args["all"]?.Value<bool>() ?? false;

            if (isAll)
            {
                var typeFilter = args["type"]?.ToString()?.Trim();
                var replayed = await _deadLetterService.RetryAllDeadLettersAsync(tenantId, typeFilter);
                return McpToolResults.Success(new
                {
                    success = true,
                    all = true,
                    replayedCount = replayed,
                    message = $"Replayed {replayed} dead letters for re-dispatch."
                });
            }

            var idStr = args["id"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(idStr) || !Guid.TryParse(idStr, out var id))
            {
                return McpToolResults.Fail("MCP-ARG-001", "id is required (or set all: true to replay all).");
            }

            var success = await _deadLetterService.RetryDeadLetterAsync(id, tenantId);
            if (!success)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Dead letter '{id}' not found.");
            }

            return McpToolResults.Success(new
            {
                success = true,
                id = id,
                message = $"Dead letter '{id}' has been scheduled for immediate re-dispatch."
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to retry dead letter: {ex.Message}");
        }
    }

    public async Task<CallToolResult> PurgeDeadLetter(JObject args)
    {
        try
        {
            var tenantId = ResolveOptionalTenant(args);
            var idStr = args["id"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(idStr) || !Guid.TryParse(idStr, out var id))
            {
                return McpToolResults.Fail("MCP-ARG-001", "id is required and must be a valid UUID.");
            }

            var success = await _deadLetterService.PurgeDeadLetterAsync(id, tenantId);
            if (!success)
            {
                return McpToolResults.Fail("MCP-NOTFOUND-001", $"Dead letter '{id}' not found.");
            }

            return McpToolResults.Success(new
            {
                success = true,
                id = id,
                message = $"Dead letter '{id}' purged successfully."
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to purge dead letter: {ex.Message}");
        }
    }

    private static Guid? ResolveOptionalTenant(JObject args)
    {
        var tenantStr = args["tenantId"]?.ToString()?.Trim();
        if (!string.IsNullOrWhiteSpace(tenantStr) && Guid.TryParse(tenantStr, out var tid))
        {
            return tid;
        }

        if (McpRequestContext.TenantId != Guid.Empty)
        {
            return McpRequestContext.TenantId;
        }

        return null;
    }

    private static object TryParsePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return string.Empty;
        try
        {
            return JToken.Parse(payload);
        }
        catch
        {
            return payload;
        }
    }
}
