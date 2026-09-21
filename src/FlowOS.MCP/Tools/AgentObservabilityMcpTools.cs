using System.Globalization;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Domain.Enums;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace FlowOS.MCP.Tools;

public sealed class AgentObservabilityMcpTools
{
    private readonly IAgentObservabilityQueryService _observability;
    private readonly TimeProvider _timeProvider;

    public AgentObservabilityMcpTools(
        IAgentObservabilityQueryService observability,
        TimeProvider timeProvider)
    {
        _observability = observability;
        _timeProvider = timeProvider;
    }

    public async Task<CallToolResult> GetAgentExecutionHistory(
        JObject args,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            Guid? workflowInstanceId = null;
            var instanceText = args["workflowInstanceId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(instanceText))
            {
                if (!Guid.TryParse(instanceText, out var parsedInstance) ||
                    parsedInstance == Guid.Empty)
                {
                    return McpToolResults.Fail(
                        "MCP-ARG-002",
                        "workflowInstanceId must be a valid UUID.");
                }
                workflowInstanceId = parsedInstance;
            }

            var limit = args["limit"]?.Value<int>()
                ?? AgentObservabilityLimits.DefaultHistoryLimit;
            if (limit is < 1 or > AgentObservabilityLimits.MaximumHistoryLimit)
            {
                return McpToolResults.Fail(
                    "MCP-ARG-001",
                    $"limit must be between 1 and {AgentObservabilityLimits.MaximumHistoryLimit}.");
            }
            if (!TryStatus(args["status"]?.ToString(), out var status))
            {
                return McpToolResults.Fail(
                    "MCP-ARG-001",
                    "status must be Running, Succeeded, Failed, or Cancelled.");
            }

            var toUtc = _timeProvider.GetUtcNow().UtcDateTime;
            if (!TryOptionalUtc(args, "toUtc", ref toUtc, out var error))
                return McpToolResults.Fail("MCP-ARG-001", error!);
            var fromUtc = toUtc.Subtract(AgentObservabilityLimits.DefaultHistoryWindow);
            if (!TryOptionalUtc(args, "fromUtc", ref fromUtc, out error))
                return McpToolResults.Fail("MCP-ARG-001", error!);
            if (!ValidateWindow(fromUtc, toUtc, out error))
                return McpToolResults.Fail("MCP-ARG-001", error!);

            var result = await _observability.GetExecutionHistoryAsync(
                new AgentExecutionHistoryRequest(
                    tenantId,
                    fromUtc,
                    toUtc,
                    workflowInstanceId,
                    status,
                    limit),
                cancellationToken);
            return McpToolResults.Success(ToCamelCase(result));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail(
                "MCP-INTERNAL",
                "Failed to read agent execution history.");
        }
    }

    public async Task<CallToolResult> GetAgentEvaluationMetrics(
        JObject args,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            if (!TryRequiredUtc(args, "fromUtc", out var fromUtc, out var error) ||
                !TryRequiredUtc(args, "toUtc", out var toUtc, out error) ||
                !ValidateWindow(fromUtc, toUtc, out error))
            {
                return McpToolResults.Fail("MCP-ARG-001", error!);
            }

            var result = await _observability.GetEvaluationMetricsAsync(
                new AgentEvaluationMetricsRequest(tenantId, fromUtc, toUtc),
                cancellationToken);
            return McpToolResults.Success(ToCamelCase(result));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return McpToolResults.Fail("MCP-LIMIT-001", ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail(
                "MCP-INTERNAL",
                "Failed to calculate agent evaluation metrics.");
        }
    }

    private static JToken ToCamelCase(object value) =>
        JToken.FromObject(
            value,
            JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            }));

    private static bool TryOptionalUtc(
        JObject args,
        string name,
        ref DateTime value,
        out string? error)
    {
        var token = args[name];
        if (token == null || token.Type == JTokenType.Null)
        {
            error = null;
            return true;
        }
        if (!TryParseUtc(token, name, out var parsed, out error))
            return false;

        value = parsed;
        return true;
    }

    private static bool TryRequiredUtc(
        JObject args,
        string name,
        out DateTime value,
        out string? error)
    {
        var token = args[name];
        if (token == null ||
            token.Type == JTokenType.Null ||
            string.IsNullOrWhiteSpace(token.ToString()))
        {
            value = default;
            error = $"{name} is required.";
            return false;
        }

        return TryParseUtc(token, name, out value, out error);
    }

    private static bool TryParseUtc(
        JToken token,
        string name,
        out DateTime value,
        out string? error)
    {
        if (token is JValue { Value: DateTimeOffset dateTimeOffset })
        {
            if (dateTimeOffset.Offset == TimeSpan.Zero)
            {
                value = dateTimeOffset.UtcDateTime;
                error = null;
                return true;
            }
        }
        else if (token is JValue { Value: DateTime dateTime })
        {
            if (dateTime.Kind == DateTimeKind.Utc)
            {
                value = dateTime;
                error = null;
                return true;
            }
        }
        else if (DateTimeOffset.TryParse(
                token.ToString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed) &&
            parsed.Offset == TimeSpan.Zero)
        {
            value = parsed.UtcDateTime;
            error = null;
            return true;
        }

        value = default;
        error = $"{name} must be an ISO-8601 UTC timestamp.";
        return false;
    }

    private static bool ValidateWindow(
        DateTime fromUtc,
        DateTime toUtc,
        out string? error)
    {
        if (fromUtc >= toUtc)
        {
            error = "fromUtc must be earlier than toUtc.";
            return false;
        }
        if (toUtc - fromUtc > AgentObservabilityLimits.MaximumWindow)
        {
            error =
                $"The UTC window cannot exceed {AgentObservabilityLimits.MaximumWindow.TotalDays:0} days.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryStatus(
        string? value,
        out AgentExecutionStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (!Enum.TryParse<AgentExecutionStatus>(
                value,
                ignoreCase: true,
                out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            return false;
        }

        status = parsed;
        return true;
    }
}
