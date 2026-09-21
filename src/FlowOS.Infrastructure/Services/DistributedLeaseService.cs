using System.Data;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public sealed class DistributedLeaseService : IDistributedLeaseService
{
    private readonly FlowOSDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public DistributedLeaseService(FlowOSDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DistributedLeaseHandle?> TryAcquireAsync(
        string leaseKey,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leaseKey))
            throw new ArgumentException("Lease key is required.", nameof(leaseKey));
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Owner id is required.", nameof(ownerId));
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        var normalizedKey = leaseKey.Trim();
        var normalizedOwner = ownerId.Trim();
        var now = UtcNow();
        var expiresAt = now.Add(duration);

        if (IsNpgsql)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var affected = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "DistributedLeases"
                    ("LeaseKey", "OwnerId", "AcquiredAtUtc", "ExpiresAtUtc")
                VALUES
                    ({normalizedKey}, {normalizedOwner}, {now}, {expiresAt})
                ON CONFLICT ("LeaseKey") DO UPDATE
                SET "OwnerId" = EXCLUDED."OwnerId",
                    "AcquiredAtUtc" = EXCLUDED."AcquiredAtUtc",
                    "ExpiresAtUtc" = EXCLUDED."ExpiresAtUtc"
                WHERE "DistributedLeases"."ExpiresAtUtc" <= {now}
                """, cancellationToken);

            if (affected == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var persisted = await _dbContext.DistributedLeases
                .AsNoTracking()
                .SingleAsync(x => x.LeaseKey == normalizedKey, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToHandle(persisted);
        }

        var lease = await _dbContext.DistributedLeases
            .SingleOrDefaultAsync(x => x.LeaseKey == normalizedKey, cancellationToken);
        if (lease == null)
        {
            lease = new DistributedLease(normalizedKey, normalizedOwner, now, expiresAt);
            _dbContext.DistributedLeases.Add(lease);
        }
        else if (!lease.TryAcquire(normalizedOwner, now, expiresAt))
        {
            return null;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ToHandle(lease);
        }
        catch (Exception exception) when (
            exception is DbUpdateException or ArgumentException or InvalidOperationException)
        {
            _dbContext.Entry(lease).State = EntityState.Detached;
            return null;
        }
    }

    public async Task<bool> ReleaseAsync(
        string leaseKey,
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leaseKey) || string.IsNullOrWhiteSpace(ownerId))
            return false;

        var normalizedKey = leaseKey.Trim();
        var normalizedOwner = ownerId.Trim();

        if (IsNpgsql)
        {
            var affected = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "DistributedLeases"
                WHERE "LeaseKey" = {normalizedKey}
                  AND "OwnerId" = {normalizedOwner}
                """, cancellationToken);
            return affected == 1;
        }

        var lease = await _dbContext.DistributedLeases
            .SingleOrDefaultAsync(
                x => x.LeaseKey == normalizedKey && x.OwnerId == normalizedOwner,
                cancellationToken);
        if (lease == null)
            return false;

        _dbContext.DistributedLeases.Remove(lease);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static DistributedLeaseHandle ToHandle(DistributedLease lease) =>
        new(lease.LeaseKey, lease.OwnerId, lease.AcquiredAtUtc, lease.ExpiresAtUtc);

    private bool IsNpgsql =>
        _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
