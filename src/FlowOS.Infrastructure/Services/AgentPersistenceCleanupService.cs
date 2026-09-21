using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public sealed class AgentPersistenceCleanupService : IAgentPersistenceCleanupService
{
    private readonly FlowOSDbContext _dbContext;
    private readonly AgentPersistenceRetentionOptions _options;
    private readonly TimeProvider _timeProvider;

    public AgentPersistenceCleanupService(
        FlowOSDbContext dbContext,
        AgentPersistenceRetentionOptions options,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AgentPersistenceCleanupResult> CleanupAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var batchSize = Math.Clamp(_options.BatchSize, 1, 10_000);
        var jobCutoff = now.AddDays(-Math.Max(0, _options.TerminalJobDays));
        var leaseCutoff = now.AddMinutes(-Math.Max(0, _options.ExpiredLeaseGraceMinutes));
        var usageCutoff = DateOnly.FromDateTime(
            now.AddDays(-Math.Max(0, _options.UsageDays)));
        var executionCutoff = now.AddDays(-Math.Max(0, _options.ExecutionDays));

        var jobsDeleted = await DeleteTerminalJobsAsync(jobCutoff, batchSize, cancellationToken);
        var leasesDeleted = await DeleteExpiredLeasesAsync(leaseCutoff, batchSize, cancellationToken);
        var usageDeleted = await DeleteOldUsageAsync(usageCutoff, batchSize, cancellationToken);
        var executionsDeleted = await DeleteOldExecutionsAsync(executionCutoff, batchSize, cancellationToken);

        return new AgentPersistenceCleanupResult(
            jobsDeleted,
            leasesDeleted,
            usageDeleted,
            executionsDeleted);
    }

    private async Task<int> DeleteTerminalJobsAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var rows = await _dbContext.AgentTaskJobs
                .Where(x =>
                    (x.Status == AgentTaskJobStatus.Completed ||
                     x.Status == AgentTaskJobStatus.DeadLettered ||
                     x.Status == AgentTaskJobStatus.Cancelled) &&
                    x.CompletedAtUtc < cutoffUtc)
                .OrderBy(x => x.CompletedAtUtc)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (rows.Count == 0)
                return total;

            _dbContext.AgentTaskJobs.RemoveRange(rows);
            await _dbContext.SaveChangesAsync(cancellationToken);
            total += rows.Count;
        }
    }

    private async Task<int> DeleteExpiredLeasesAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (IsNpgsql)
        {
            var postgresTotal = 0;
            while (true)
            {
                var deleted = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "DistributedLeases"
                    WHERE "LeaseKey" IN (
                        SELECT "LeaseKey"
                        FROM "DistributedLeases"
                        WHERE "ExpiresAtUtc" < {cutoffUtc}
                        ORDER BY "ExpiresAtUtc", "LeaseKey"
                        LIMIT {batchSize}
                        FOR UPDATE SKIP LOCKED
                    )
                    """, cancellationToken);
                if (deleted == 0)
                    return postgresTotal;

                postgresTotal += deleted;
            }
        }

        var total = 0;
        while (true)
        {
            var rows = await _dbContext.DistributedLeases
                .Where(x => x.ExpiresAtUtc < cutoffUtc)
                .OrderBy(x => x.ExpiresAtUtc)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (rows.Count == 0)
                return total;

            _dbContext.DistributedLeases.RemoveRange(rows);
            await _dbContext.SaveChangesAsync(cancellationToken);
            total += rows.Count;
        }
    }

    private async Task<int> DeleteOldUsageAsync(
        DateOnly cutoffDateUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var rows = await _dbContext.HostedLlmDailyUsages
                .Where(x => x.UsageDateUtc < cutoffDateUtc)
                .OrderBy(x => x.UsageDateUtc)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (rows.Count == 0)
                return total;

            _dbContext.HostedLlmDailyUsages.RemoveRange(rows);
            await _dbContext.SaveChangesAsync(cancellationToken);
            total += rows.Count;
        }
    }

    private async Task<int> DeleteOldExecutionsAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var rows = await _dbContext.AgentExecutionRecords
                .Where(x => x.StartedAtUtc < cutoffUtc)
                .OrderBy(x => x.StartedAtUtc)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            if (rows.Count == 0)
                return total;

            _dbContext.AgentExecutionRecords.RemoveRange(rows);
            await _dbContext.SaveChangesAsync(cancellationToken);
            total += rows.Count;
        }
    }

    private bool IsNpgsql =>
        _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
}
