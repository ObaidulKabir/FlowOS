using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public class WorkflowActionHistoryService : IWorkflowActionHistoryService
{
    private readonly FlowOSDbContext _dbContext;

    public WorkflowActionHistoryService(FlowOSDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<WorkflowActionExecutionLogDto>> GetActionHistoryAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? stepId = null,
        string? actionType = null,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _dbContext.ActionExecutionLogs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.WorkflowInstanceId == workflowInstanceId);

        if (!string.IsNullOrWhiteSpace(stepId))
        {
            query = query.Where(x => x.StepId == stepId);
        }

        if (!string.IsNullOrWhiteSpace(actionType))
        {
            query = query.Where(x => x.ActionType == actionType);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        var logs = await query
            .OrderByDescending(x => x.ExecutedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return logs.Select(MapToDto).ToList();
    }

    public async Task<WorkflowActionExecutionLogDto?> GetActionLogByIdAsync(
        Guid logId,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.ActionExecutionLogs.AsNoTracking().Where(x => x.Id == logId);
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            query = query.Where(x => x.TenantId == tenantId.Value);
        }

        var log = await query.FirstOrDefaultAsync(ct);
        return log == null ? null : MapToDto(log);
    }

    private static WorkflowActionExecutionLogDto MapToDto(WorkflowActionExecutionLog log)
    {
        return new WorkflowActionExecutionLogDto(
            log.Id,
            log.TenantId,
            log.WorkflowInstanceId,
            log.StepId,
            log.TriggerPhase,
            log.ActionType,
            log.Target,
            log.Status,
            log.ExecutedAtUtc,
            log.DurationMs,
            log.HttpStatusCode,
            log.RequestPayloadSnippet,
            log.ResponseSnippet,
            log.ErrorMessage,
            log.AttemptNumber,
            log.OutboxMessageId
        );
    }
}
