using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities.ExternalAI;
using FlowOS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

internal sealed class ExternalAgentChangeStore : IExternalAgentChangeStore
{
    private readonly FlowOSDbContext _db;

    public ExternalAgentChangeStore(FlowOSDbContext db) => _db = db;

    public async Task<IReadOnlyList<ExternalAgentChangeRecord>> LeaseNextAsync(
        Guid tenantId,
        string agentId,
        TimeSpan ttl,
        int limit,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var candidates = await _db.ExternalAgentChangeRecords
            .Where(c =>
                c.TenantId == tenantId &&
                (c.Status == ExternalAgentChangeStatus.Pending ||
                 (c.Status == ExternalAgentChangeStatus.Failed && c.NextRetryUtc <= now)) &&
                (c.LeasedUntilUtc == null || c.LeasedUntilUtc <= now))
            .OrderBy(c => c.CreatedAtUtc)
            .ThenBy(c => c.Id)
            .Take(limit)
            .ToListAsync(ct);

        foreach (var c in candidates)
        {
            c.MarkLeased(agentId, ttl);
        }

        await _db.SaveChangesAsync(ct);
        return candidates;
    }

    public async Task AckAsync(
        Guid changeId,
        bool succeeded,
        string? error,
        CancellationToken ct = default)
    {
        var change = await _db.ExternalAgentChangeRecords.FindAsync([changeId], ct);
        if (change == null)
            return;

        if (succeeded)
        {
            if (change.Status == ExternalAgentChangeStatus.Processed)
                return;
            if (change.Status != ExternalAgentChangeStatus.Leased)
                return;
            change.MarkProcessed();
        }
        else
        {
            if (change.Status == ExternalAgentChangeStatus.DeadLetter)
                return;
            if (change.Status != ExternalAgentChangeStatus.Leased)
                return;
            change.MarkFailed(error ?? "Unknown error", DateTime.UtcNow);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task RenewLeaseAsync(
        Guid changeId,
        string agentId,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        var change = await _db.ExternalAgentChangeRecords.FindAsync([changeId], ct);
        if (change == null || change.Status != ExternalAgentChangeStatus.Leased)
            return;

        if (change.LeasedByAgent != agentId)
            return;

        change.RenewLease(ttl);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ExternalAgentChangeRecord>> ListPendingAsync(
        Guid tenantId,
        int limit,
        CancellationToken ct = default)
    {
        return await _db.ExternalAgentChangeRecords
            .Where(c =>
                c.TenantId == tenantId &&
                (c.Status == ExternalAgentChangeStatus.Pending ||
                 c.Status == ExternalAgentChangeStatus.Failed))
            .OrderBy(c => c.CreatedAtUtc)
            .ThenBy(c => c.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ExternalAgentChangeRecord>> ListAsync(
        Guid tenantId,
        ExternalAgentChangeStatus? status,
        int limit,
        CancellationToken ct = default)
    {
        var query = _db.ExternalAgentChangeRecords
            .Where(c => c.TenantId == tenantId);

        if (status.HasValue)
        {
            query = query.Where(c => c.Status == status.Value);
        }

        return await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> ReclaimExpiredLeasesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expired = await _db.ExternalAgentChangeRecords
            .Where(c =>
                c.Status == ExternalAgentChangeStatus.Leased &&
                c.LeasedUntilUtc <= now)
            .ToListAsync(ct);

        foreach (var c in expired)
        {
            c.ReclaimLease();
        }

        if (expired.Count > 0)
            await _db.SaveChangesAsync(ct);

        return expired.Count;
    }

    public Task AddAsync(ExternalAgentChangeRecord record, CancellationToken ct = default)
    {
        _db.ExternalAgentChangeRecords.Add(record);
        return _db.SaveChangesAsync(ct);
    }

    public Task<ExternalAgentChangeRecord?> GetByIdAsync(Guid changeId, CancellationToken ct = default)
    {
        return _db.ExternalAgentChangeRecords.FindAsync([changeId], ct).AsTask();
    }
}
