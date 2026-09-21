using System.Data;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public sealed class HostedLlmUsageStore : IHostedLlmUsageStore
{
    private readonly FlowOSDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public HostedLlmUsageStore(FlowOSDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<HostedLlmUsageReservation> TryReserveAsync(
        Guid tenantId,
        DateOnly usageDateUtc,
        string model,
        int maximumRequests,
        int requestCount = 1,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(tenantId, model);
        if (maximumRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRequests));
        if (requestCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestCount));

        var normalizedModel = model.Trim();
        if (requestCount > maximumRequests)
        {
            var current = await ReadAsync(
                tenantId,
                usageDateUtc,
                normalizedModel,
                cancellationToken);
            return new HostedLlmUsageReservation(false, maximumRequests, current);
        }

        var now = UtcNow();
        if (IsNpgsql)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var id = Guid.NewGuid();
            var affected = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "HostedLlmDailyUsages"
                    ("Id", "TenantId", "UsageDateUtc", "Model", "ReservedRequests",
                     "FinalizedRequests", "SuccessfulRequests", "FailedRequests",
                     "InputTokens", "OutputTokens", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({id}, {tenantId}, {usageDateUtc}, {normalizedModel}, {requestCount},
                     0, 0, 0, 0, 0, {now}, {now})
                ON CONFLICT ("TenantId", "UsageDateUtc", "Model") DO UPDATE
                SET "ReservedRequests" = "HostedLlmDailyUsages"."ReservedRequests" + {requestCount},
                    "UpdatedAtUtc" = {now}
                WHERE "HostedLlmDailyUsages"."ReservedRequests"
                    + "HostedLlmDailyUsages"."FinalizedRequests"
                    + {requestCount} <= {maximumRequests}
                """, cancellationToken);

            var usage = await _dbContext.HostedLlmDailyUsages
                .AsNoTracking()
                .SingleAsync(
                    x => x.TenantId == tenantId &&
                         x.UsageDateUtc == usageDateUtc &&
                         x.Model == normalizedModel,
                    cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new HostedLlmUsageReservation(
                affected == 1,
                maximumRequests,
                ToSnapshot(usage));
        }

        var entity = await _dbContext.HostedLlmDailyUsages
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId &&
                     x.UsageDateUtc == usageDateUtc &&
                     x.Model == normalizedModel,
                cancellationToken);
        if (entity == null)
        {
            entity = new HostedLlmDailyUsage(tenantId, usageDateUtc, normalizedModel, now);
            _dbContext.HostedLlmDailyUsages.Add(entity);
        }

        var allowed = entity.TryReserve(requestCount, maximumRequests, now);
        if (allowed)
            await _dbContext.SaveChangesAsync(cancellationToken);

        return new HostedLlmUsageReservation(allowed, maximumRequests, ToSnapshot(entity));
    }

    public async Task<HostedLlmUsageSnapshot> FinalizeAsync(
        Guid tenantId,
        DateOnly usageDateUtc,
        string model,
        bool succeeded,
        long inputTokens = 0,
        long outputTokens = 0,
        int requestCount = 1,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(tenantId, model);
        if (requestCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestCount));
        if (inputTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(inputTokens));
        if (outputTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(outputTokens));

        var normalizedModel = model.Trim();
        var now = UtcNow();

        if (IsNpgsql)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var affected = succeeded
                ? await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "HostedLlmDailyUsages"
                    SET "ReservedRequests" = "ReservedRequests" - {requestCount},
                        "FinalizedRequests" = "FinalizedRequests" + {requestCount},
                        "SuccessfulRequests" = "SuccessfulRequests" + {requestCount},
                        "InputTokens" = "InputTokens" + {inputTokens},
                        "OutputTokens" = "OutputTokens" + {outputTokens},
                        "UpdatedAtUtc" = {now}
                    WHERE "TenantId" = {tenantId}
                      AND "UsageDateUtc" = {usageDateUtc}
                      AND "Model" = {normalizedModel}
                      AND "ReservedRequests" >= {requestCount}
                    """, cancellationToken)
                : await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "HostedLlmDailyUsages"
                    SET "ReservedRequests" = "ReservedRequests" - {requestCount},
                        "FinalizedRequests" = "FinalizedRequests" + {requestCount},
                        "FailedRequests" = "FailedRequests" + {requestCount},
                        "InputTokens" = "InputTokens" + {inputTokens},
                        "OutputTokens" = "OutputTokens" + {outputTokens},
                        "UpdatedAtUtc" = {now}
                    WHERE "TenantId" = {tenantId}
                      AND "UsageDateUtc" = {usageDateUtc}
                      AND "Model" = {normalizedModel}
                      AND "ReservedRequests" >= {requestCount}
                    """, cancellationToken);

            if (affected != 1)
                throw new InvalidOperationException("No matching hosted LLM reservation could be finalized.");

            var persisted = await _dbContext.HostedLlmDailyUsages
                .AsNoTracking()
                .SingleAsync(
                    x => x.TenantId == tenantId &&
                         x.UsageDateUtc == usageDateUtc &&
                         x.Model == normalizedModel,
                    cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToSnapshot(persisted);
        }

        var usage = await _dbContext.HostedLlmDailyUsages
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId &&
                     x.UsageDateUtc == usageDateUtc &&
                     x.Model == normalizedModel,
                cancellationToken)
            ?? throw new InvalidOperationException("No matching hosted LLM reservation could be finalized.");
        usage.FinalizeReservation(requestCount, succeeded, inputTokens, outputTokens, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToSnapshot(usage);
    }

    public async Task<HostedLlmUsageSnapshot?> ReadAsync(
        Guid tenantId,
        DateOnly usageDateUtc,
        string model,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(tenantId, model);
        var normalizedModel = model.Trim();
        var usage = await _dbContext.HostedLlmDailyUsages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId &&
                     x.UsageDateUtc == usageDateUtc &&
                     x.Model == normalizedModel,
                cancellationToken);
        return usage == null ? null : ToSnapshot(usage);
    }

    private static HostedLlmUsageSnapshot ToSnapshot(HostedLlmDailyUsage usage) =>
        new(
            usage.TenantId,
            usage.UsageDateUtc,
            usage.Model,
            usage.ReservedRequests,
            usage.FinalizedRequests,
            usage.SuccessfulRequests,
            usage.FailedRequests,
            usage.InputTokens,
            usage.OutputTokens,
            usage.UpdatedAtUtc);

    private static void ValidateKey(Guid tenantId, string model)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required.", nameof(model));
        if (model.Trim().Length > 200)
            throw new ArgumentException("Model cannot exceed 200 characters.", nameof(model));
    }

    private bool IsNpgsql =>
        _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
