using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public sealed class AgentAutomationMcpTools
{
    private readonly IConversationStore _conversationStore;
    private readonly IAgentTaskCoordinator _taskCoordinator;
    private readonly FlowOSDbContext _dbContext;

    public AgentAutomationMcpTools(
        IConversationStore conversationStore,
        IAgentTaskCoordinator taskCoordinator,
        FlowOSDbContext dbContext)
    {
        _conversationStore = conversationStore;
        _taskCoordinator = taskCoordinator;
        _dbContext = dbContext;
    }

    public async Task<CallToolResult> AppendAgentChatMessage(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var workflowInstanceId = ParseGuid(args, "workflowInstanceId");
            var stepId = RequireString(args, "stepId", 200);
            var message = RequireString(args, "message");
            var name = OptionalString(args, "name", 200);
            var triggerAgent = args["triggerAgent"]?.Value<bool>() ?? true;

            await _conversationStore.AppendMessageAsync(
                tenantId,
                workflowInstanceId,
                stepId,
                new ChatMessage(ChatMessageRole.User, message, name),
                CancellationToken.None);

            IReadOnlyList<AgentTaskEnqueueResult> enqueued = Array.Empty<AgentTaskEnqueueResult>();
            if (triggerAgent)
            {
                enqueued = await _taskCoordinator.EnqueueCurrentStepAsync(
                    tenantId,
                    workflowInstanceId,
                    AgentTaskSource.McpRequest,
                    CancellationToken.None);
            }

            return McpToolResults.Success(new
            {
                tenantId,
                workflowInstanceId,
                stepId,
                triggerAgent,
                enqueuedJobs = enqueued.Select(item => new
                {
                    jobId = item.JobId,
                    created = item.Created,
                    status = item.Status.ToString(),
                    activeKey = item.ActiveKey
                }).ToList(),
                messageRecorded = true
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to append agent chat message: {ex.Message}");
        }
    }

    public async Task<CallToolResult> GetAgentChatHistory(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var workflowInstanceId = ParseGuid(args, "workflowInstanceId");
            var stepId = RequireString(args, "stepId", 200);

            var history = await _conversationStore.GetHistoryAsync(
                tenantId,
                workflowInstanceId,
                stepId,
                CancellationToken.None);

            return McpToolResults.Success(new
            {
                tenantId,
                workflowInstanceId,
                stepId,
                count = history.Count,
                messages = history.Select(message => new
                {
                    role = message.Role.ToString(),
                    content = message.Content,
                    name = message.Name,
                    timestamp = message.Timestamp
                }).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to get agent chat history: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ListAgentChatSessions(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var limit = args["limit"]?.Value<int>() ?? 50;
            if (limit is < 1 or > 200)
                return McpToolResults.Fail("MCP-ARG-001", "limit must be between 1 and 200.");

            var sessions = await _conversationStore.ListSessionsAsync(
                tenantId,
                limit,
                CancellationToken.None);

            return McpToolResults.Success(new
            {
                tenantId,
                limit,
                totalCount = sessions.Count,
                sessions = sessions.Select(session => new
                {
                    workflowInstanceId = session.WorkflowInstanceId,
                    stepId = session.StepId,
                    messageCount = session.MessageCount,
                    lastMessageAtUtc = session.LastMessageAtUtc
                }).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to list agent chat sessions: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ListAgentPromptAudits(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var limit = args["limit"]?.Value<int>() ?? 50;
            if (limit is < 1 or > 200)
                return McpToolResults.Fail("MCP-ARG-001", "limit must be between 1 and 200.");

            var includePayloads = args["includePayloads"]?.Value<bool>() ?? false;
            var workflowInstanceId = ParseGuidOptional(args, "workflowInstanceId");
            var executionId = ParseGuidOptional(args, "executionId");
            var stepId = OptionalString(args, "stepId", 200);
            var providerAlias = OptionalString(args, "providerAlias", 200);
            var fromUtc = ParseUtcOptional(args, "fromUtc");
            var toUtc = ParseUtcOptional(args, "toUtc");

            IQueryable<AgentPromptAuditRecord> query = _dbContext.AgentPromptAuditRecords
                .AsNoTracking()
                .Where(record => record.TenantId == tenantId);

            if (workflowInstanceId.HasValue)
                query = query.Where(record => record.WorkflowInstanceId == workflowInstanceId.Value);
            if (executionId.HasValue)
                query = query.Where(record => record.ExecutionId == executionId.Value);
            if (!string.IsNullOrWhiteSpace(stepId))
                query = query.Where(record => record.StepId == stepId);
            if (!string.IsNullOrWhiteSpace(providerAlias))
                query = query.Where(record => record.ProviderAlias == providerAlias);
            if (fromUtc.HasValue)
                query = query.Where(record => record.RecordedAtUtc >= fromUtc.Value);
            if (toUtc.HasValue)
                query = query.Where(record => record.RecordedAtUtc < toUtc.Value);

            var records = await query
                .OrderByDescending(record => record.RecordedAtUtc)
                .Take(limit)
                .ToListAsync();

            return McpToolResults.Success(new
            {
                tenantId,
                includePayloads,
                totalCount = records.Count,
                audits = records.Select(record => new
                {
                    auditId = record.AuditId,
                    workflowInstanceId = record.WorkflowInstanceId,
                    stepId = record.StepId,
                    providerAlias = record.ProviderAlias,
                    providerName = record.ProviderName,
                    model = record.Model,
                    recordedAtUtc = record.RecordedAtUtc,
                    iteration = record.Iteration,
                    failureCode = record.FailureCode,
                    httpStatusCode = record.HttpStatusCode,
                    inputTokens = record.InputTokens,
                    outputTokens = record.OutputTokens,
                    durationMs = record.DurationMs,
                    executionId = record.ExecutionId,
                    correlationId = record.CorrelationId,
                    systemPrompt = includePayloads ? record.SystemPrompt : null,
                    userPrompt = includePayloads ? record.UserPrompt : null,
                    rawRequestPayload = includePayloads ? record.RawRequestPayload : null,
                    rawResponsePayload = includePayloads ? record.RawResponsePayload : null
                }).ToList()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return McpToolResults.Fail("MCP-ARG-001", ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to list agent prompt audits: {ex.Message}");
        }
    }

    public async Task<CallToolResult> GetExternalAgentSettings(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var tenant = await _dbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.TenantId == tenantId);
            if (tenant == null)
                return McpToolResults.Fail("MCP-NOTFOUND-001", "Tenant was not found.");

            return McpToolResults.Success(ToExternalAgentSettingsDto(tenant));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to load external agent settings: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ConfigureExternalAgentSettings(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            if (args["enabled"] == null)
                return McpToolResults.Fail("MCP-ARG-001", "enabled is required.");

            var enabled = args["enabled"]!.Value<bool>();
            var autoPilot = enabled && (args["autoPilot"]?.Value<bool>() ?? false);
            var agentProfileId = OptionalString(args, "agentProfileId", 200);

            var tenant = await _dbContext.Tenants
                .FirstOrDefaultAsync(item => item.TenantId == tenantId);
            if (tenant == null)
                return McpToolResults.Fail("MCP-NOTFOUND-001", "Tenant was not found.");

            tenant.SetExternalAIAgent(enabled, autoPilot, agentProfileId);
            await _dbContext.SaveChangesAsync();

            return McpToolResults.Success(ToExternalAgentSettingsDto(tenant));
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to update external agent settings: {ex.Message}");
        }
    }

    private static object ToExternalAgentSettingsDto(Tenant tenant) => new
    {
        tenantId = tenant.TenantId,
        enabled = tenant.ExternalAIAgentEnabled ?? false,
        autoPilot = tenant.ExternalAIAgentAutoPilot ?? false,
        agentProfileId = tenant.ExternalAIAgentProfileId
    };

    private static Guid ParseGuid(JObject args, string propertyName)
    {
        var value = args[propertyName]?.ToString();
        if (string.IsNullOrWhiteSpace(value) || !Guid.TryParse(value, out var parsed))
            throw new ArgumentException($"{propertyName} must be a valid UUID.", propertyName);
        return parsed;
    }

    private static Guid? ParseGuidOptional(JObject args, string propertyName)
    {
        var value = args[propertyName]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Guid.TryParse(value, out var parsed))
            throw new ArgumentException($"{propertyName} must be a valid UUID.", propertyName);
        return parsed;
    }

    private static DateTime? ParseUtcOptional(JObject args, string propertyName)
    {
        var value = args[propertyName]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!DateTimeOffset.TryParse(value, out var parsed))
            throw new ArgumentException($"{propertyName} must be a valid ISO-8601 timestamp.", propertyName);
        return parsed.UtcDateTime;
    }

    private static string RequireString(JObject args, string propertyName, int? maxLength = null)
    {
        var value = OptionalString(args, propertyName, maxLength);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{propertyName} is required.", propertyName);
        return value;
    }

    private static string? OptionalString(JObject args, string propertyName, int? maxLength = null)
    {
        var value = args[propertyName]?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (maxLength.HasValue && value.Length > maxLength.Value)
            return value[..maxLength.Value];
        return value;
    }
}
