using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Core.Common.Interfaces;

public record WorkflowActionExecutionLogDto(
    Guid Id,
    Guid TenantId,
    Guid WorkflowInstanceId,
    string StepId,
    string TriggerPhase,
    string ActionType,
    string? Target,
    string Status,
    DateTime ExecutedAtUtc,
    long DurationMs,
    int? HttpStatusCode,
    string? RequestPayloadSnippet,
    string? ResponseSnippet,
    string? ErrorMessage,
    int AttemptNumber,
    Guid? OutboxMessageId
);

public interface IWorkflowActionHistoryService
{
    Task<IReadOnlyList<WorkflowActionExecutionLogDto>> GetActionHistoryAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? stepId = null,
        string? actionType = null,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default);

    Task<WorkflowActionExecutionLogDto?> GetActionLogByIdAsync(
        Guid logId,
        Guid? tenantId = null,
        CancellationToken ct = default);
}
