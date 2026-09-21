using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public sealed class AgentExecutionStore :
    IAgentExecutionRecorder,
    IAgentExecutionHistoryStore
{
    private readonly FlowOSDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public AgentExecutionStore(FlowOSDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Guid> StartAsync(
        AgentExecutionStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executionId = request.ExecutionId ?? Guid.NewGuid();
        var existing = await _dbContext.AgentExecutionRecords
            .AsNoTracking()
            .AnyAsync(x => x.ExecutionId == executionId, cancellationToken);
        if (existing)
            return executionId;

        var record = new AgentExecutionRecord(
            executionId,
            request.JobId,
            request.TenantId,
            request.WorkflowInstanceId,
            request.StepId,
            request.Actor,
            request.Mode,
            request.StartedAtUtc ?? UtcNow(),
            request.Claimant);
        record.SetProviderMetadata(
            request.ProviderAlias,
            request.ProviderName,
            request.Model,
            request.PromptAlias);
        record.SetVersionMetadata(
            request.RuntimeIdentifier,
            request.RuntimeVersion,
            request.WorkflowDefinitionId,
            request.WorkflowDefinitionVersion,
            request.ContextBindingId,
            request.ContextBindingRevisionId,
            request.ContextVersion);
        record.SetTraceIdentifiers(request.IdempotencyKey, request.CorrelationId);

        _dbContext.AgentExecutionRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return executionId;
    }

    public async Task<bool> CompleteAsync(
        Guid executionId,
        AgentExecutionCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        var record = await FindAsync(executionId, cancellationToken);
        if (record == null)
            return false;
        if (record.Status != AgentExecutionStatus.Running)
            return true;

        record.Complete(
            completion.Success,
            completion.EndedAtUtc ?? UtcNow(),
            completion.FailureCode,
            completion.SanitizedFailure,
            completion.HttpStatusCode,
            completion.InputTokens,
            completion.OutputTokens,
            completion.SuggestedEvent,
            completion.Confidence,
            completion.WasCommitted,
            completion.WasParked,
            completion.ParkReason);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CancelAsync(
        Guid executionId,
        string? sanitizedReason = null,
        CancellationToken cancellationToken = default)
    {
        var record = await FindAsync(executionId, cancellationToken);
        if (record == null)
            return false;
        if (record.Status != AgentExecutionStatus.Running)
            return true;

        record.Cancel(UtcNow(), sanitizedReason);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RecordObservedOutcomeAsync(
        Guid executionId,
        string observedOutcome,
        bool? matchedSuggestion,
        CancellationToken cancellationToken = default)
    {
        var record = await FindAsync(executionId, cancellationToken);
        if (record == null)
            return false;

        record.RecordObservedOutcome(observedOutcome, matchedSuggestion, UtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RecordOverrideAsync(
        Guid executionId,
        string overrideActor,
        string? overrideEvent,
        string overrideReason,
        CancellationToken cancellationToken = default)
    {
        var record = await FindAsync(executionId, cancellationToken);
        if (record == null)
            return false;

        record.RecordOverride(
            overrideActor,
            overrideEvent,
            overrideReason,
            UtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<AgentExecutionRecord?> GetAsync(
        Guid tenantId,
        Guid executionId,
        CancellationToken cancellationToken = default) =>
        _dbContext.AgentExecutionRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.ExecutionId == executionId,
                cancellationToken);

    public Task<AgentExecutionRecord?> GetLatestForJobAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default) =>
        _dbContext.AgentExecutionRecords
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.JobId == jobId)
            .OrderByDescending(x => x.StartedAtUtc)
            .ThenByDescending(x => x.ExecutionId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AgentExecutionRecord>> ListAsync(
        AgentExecutionHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(query));

        var limit = Math.Clamp(
            query.Limit,
            1,
            AgentObservabilityLimits.MaximumMetricRecords + 1);
        var records = _dbContext.AgentExecutionRecords
            .AsNoTracking()
            .Where(x => x.TenantId == query.TenantId);

        if (query.WorkflowInstanceId.HasValue)
            records = records.Where(x => x.WorkflowInstanceId == query.WorkflowInstanceId.Value);
        if (query.FromUtc.HasValue)
        {
            var fromUtc = AsUtc(query.FromUtc.Value);
            records = records.Where(x => x.StartedAtUtc >= fromUtc);
        }
        if (query.ToUtc.HasValue)
        {
            var toUtc = AsUtc(query.ToUtc.Value);
            records = records.Where(x => x.StartedAtUtc < toUtc);
        }
        if (query.Status.HasValue)
            records = records.Where(x => x.Status == query.Status.Value);

        return await records
            .OrderByDescending(x => x.StartedAtUtc)
            .ThenByDescending(x => x.ExecutionId)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private Task<AgentExecutionRecord?> FindAsync(
        Guid executionId,
        CancellationToken cancellationToken) =>
        _dbContext.AgentExecutionRecords
            .SingleOrDefaultAsync(x => x.ExecutionId == executionId, cancellationToken);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
