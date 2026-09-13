using System;

namespace FlowOS.Domain.Entities;

public class WorkflowActionExecutionLog
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public string StepId { get; private set; }
    public string TriggerPhase { get; private set; }
    public string ActionType { get; private set; }
    public string? Target { get; private set; }
    public string Status { get; private set; } // "Succeeded", "Failed"
    public DateTime ExecutedAtUtc { get; private set; }
    public long DurationMs { get; private set; }
    public int? HttpStatusCode { get; private set; }
    public string? RequestPayloadSnippet { get; private set; }
    public string? ResponseSnippet { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int AttemptNumber { get; private set; }
    public Guid? OutboxMessageId { get; private set; }

    // Parameterless constructor for EF Core
    private WorkflowActionExecutionLog()
    {
        StepId = string.Empty;
        TriggerPhase = string.Empty;
        ActionType = string.Empty;
        Status = string.Empty;
    }

    public WorkflowActionExecutionLog(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        string triggerPhase,
        string actionType,
        string? target,
        string status,
        long durationMs,
        int? httpStatusCode = null,
        string? requestPayloadSnippet = null,
        string? responseSnippet = null,
        string? errorMessage = null,
        int attemptNumber = 1,
        Guid? outboxMessageId = null)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        WorkflowInstanceId = workflowInstanceId;
        StepId = stepId ?? string.Empty;
        TriggerPhase = triggerPhase ?? string.Empty;
        ActionType = actionType ?? string.Empty;
        Target = target;
        Status = status ?? "Succeeded";
        ExecutedAtUtc = DateTime.UtcNow;
        DurationMs = durationMs;
        HttpStatusCode = httpStatusCode;
        RequestPayloadSnippet = Truncate(requestPayloadSnippet, 2000);
        ResponseSnippet = Truncate(responseSnippet, 2000);
        ErrorMessage = errorMessage;
        AttemptNumber = attemptNumber;
        OutboxMessageId = outboxMessageId;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength) + "... [truncated]";
    }
}
