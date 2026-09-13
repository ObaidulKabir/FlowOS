using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public class IdempotencyService : IIdempotencyService
{
    private readonly FlowOSDbContext _dbContext;

    public IdempotencyService(FlowOSDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> TryBeginAsync(
        Guid tenantId,
        string operationName,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return true;

        var existing = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.OperationName == operationName &&
                x.IdempotencyKey == idempotencyKey, ct);

        if (existing != null)
        {
            if (string.Equals(existing.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                existing.MarkPending();
                await _dbContext.SaveChangesAsync(ct);
                return true;
            }

            return false;
        }

        _dbContext.IdempotencyRecords.Add(new IdempotencyRecord(tenantId, operationName, idempotencyKey));
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task CompleteAsync<T>(
        Guid tenantId,
        string operationName,
        string idempotencyKey,
        T result,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return;

        var record = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.OperationName == operationName &&
                x.IdempotencyKey == idempotencyKey, ct);
        if (record == null) return;

        record.MarkCompleted(JsonSerializer.Serialize(result));
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task FailAsync(
        Guid tenantId,
        string operationName,
        string idempotencyKey,
        string? reason = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return;

        var record = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.OperationName == operationName &&
                x.IdempotencyKey == idempotencyKey, ct);
        if (record == null) return;

        record.MarkFailed(reason);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<(bool Found, T? Result)> TryGetCompletedResultAsync<T>(
        Guid tenantId,
        string operationName,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return (false, default);

        var record = await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.OperationName == operationName &&
                x.IdempotencyKey == idempotencyKey, ct);

        if (record == null || !string.Equals(record.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            return (false, default);

        if (string.IsNullOrWhiteSpace(record.ResultJson))
            return (true, default);

        var parsed = JsonSerializer.Deserialize<T>(record.ResultJson);
        return (true, parsed);
    }

    public async Task<IdempotencyStatusDto?> GetStatusAsync(
        Guid tenantId,
        string operationName,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        var record = await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.OperationName == operationName &&
                x.IdempotencyKey == idempotencyKey, ct);
        if (record == null) return null;

        return new IdempotencyStatusDto(
            record.TenantId,
            record.OperationName,
            record.IdempotencyKey,
            record.Status,
            record.CreatedAtUtc,
            record.UpdatedAtUtc);
    }
}
