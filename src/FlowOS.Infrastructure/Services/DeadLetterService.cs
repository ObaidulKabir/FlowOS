using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.Services;

public class DeadLetterService : IDeadLetterService
{
    private readonly FlowOSDbContext _dbContext;
    private readonly ILogger<DeadLetterService> _logger;

    public DeadLetterService(FlowOSDbContext dbContext, ILogger<DeadLetterService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DeadLetterMessageDto>> GetDeadLettersAsync(
        Guid? tenantId,
        string? typeFilter = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.IsDeadLetter || (m.ProcessedOnUtc == null && m.RetryCount >= m.MaxRetries));

        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            query = query.Where(m => m.TenantId == tenantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(typeFilter))
        {
            query = query.Where(m => m.Type.Contains(typeFilter));
        }

        var messages = await query
            .OrderByDescending(m => m.OccurredOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return messages.Select(MapToDto).ToList();
    }

    public async Task<DeadLetterMessageDto?> GetDeadLetterByIdAsync(
        Guid messageId,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.OutboxMessages.AsNoTracking().Where(m => m.Id == messageId);
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            query = query.Where(m => m.TenantId == tenantId.Value);
        }

        var message = await query.FirstOrDefaultAsync(ct);
        return message != null ? MapToDto(message) : null;
    }

    public async Task<bool> RetryDeadLetterAsync(
        Guid messageId,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.OutboxMessages.Where(m => m.Id == messageId);
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            query = query.Where(m => m.TenantId == tenantId.Value);
        }

        var message = await query.FirstOrDefaultAsync(ct);
        if (message == null) return false;

        message.ReplayFromDeadLetter();
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Dead letter outbox message {MessageId} replayed for re-dispatch", messageId);
        return true;
    }

    public async Task<int> RetryAllDeadLettersAsync(
        Guid? tenantId = null,
        string? typeFilter = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.OutboxMessages
            .Where(m => m.IsDeadLetter || (m.ProcessedOnUtc == null && m.RetryCount >= m.MaxRetries));

        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            query = query.Where(m => m.TenantId == tenantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(typeFilter))
        {
            query = query.Where(m => m.Type.Contains(typeFilter));
        }

        var messages = await query.ToListAsync(ct);
        if (!messages.Any()) return 0;

        foreach (var message in messages)
        {
            message.ReplayFromDeadLetter();
        }

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("Replayed {Count} dead letter outbox messages", messages.Count);
        return messages.Count;
    }

    public async Task<bool> PurgeDeadLetterAsync(
        Guid messageId,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        var query = _dbContext.OutboxMessages.Where(m => m.Id == messageId);
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            query = query.Where(m => m.TenantId == tenantId.Value);
        }

        var message = await query.FirstOrDefaultAsync(ct);
        if (message == null) return false;

        _dbContext.OutboxMessages.Remove(message);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Dead letter outbox message {MessageId} purged", messageId);
        return true;
    }

    private static DeadLetterMessageDto MapToDto(OutboxMessage m)
    {
        string? actionType = null;
        string? targetUrl = null;
        string? httpMethod = null;
        string? stepId = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(m.Payload))
            {
                using var doc = JsonDocument.Parse(m.Payload);
                var root = doc.RootElement;
                if (root.TryGetProperty("actionType", out var at)) actionType = at.GetString();
                if (root.TryGetProperty("url", out var u)) targetUrl = u.GetString();
                if (root.TryGetProperty("method", out var meth)) httpMethod = meth.GetString();
                if (root.TryGetProperty("stepId", out var sid)) stepId = sid.GetString();
            }
        }
        catch
        {
            // Ignored - raw payload is retained
        }

        return new DeadLetterMessageDto
        {
            Id = m.Id,
            TenantId = m.TenantId,
            Type = m.Type,
            Payload = m.Payload,
            OccurredOnUtc = m.OccurredOnUtc,
            ProcessedOnUtc = m.ProcessedOnUtc,
            Error = m.Error,
            RetryCount = m.RetryCount,
            MaxRetries = m.MaxRetries,
            NextRetryUtc = m.NextRetryUtc,
            IsDeadLetter = m.IsDeadLetter || (m.ProcessedOnUtc == null && m.RetryCount >= m.MaxRetries),
            ActionType = actionType,
            TargetUrl = targetUrl,
            HttpMethod = httpMethod,
            StepId = stepId
        };
    }
}
