using System.Text.Json;
using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Services;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Agents;

public sealed class AgentObservabilityQueryServiceTests
{
    [Fact]
    public async Task History_IsTenantScoped_AndReturnsOnlySanitizedAllowlistedFields()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        var tenantRecord = Execution(
            tenantId,
            instanceId,
            now.AddMinutes(-10),
            providerAlias: "tenant-openai");
        tenantRecord.Complete(
            false,
            now.AddMinutes(-9),
            "sk-secret-code",
            "raw failure includes sk-never-return and a response body",
            500,
            7,
            2,
            null,
            null,
            false,
            true,
            "provider response body must not be returned");
        tenantRecord.RecordOverride(
            "User:reviewer",
            "REJECT",
            "sensitive override reason",
            now.AddMinutes(-8));

        var otherRecord = Execution(
            otherTenantId,
            instanceId,
            now.AddMinutes(-5),
            providerAlias: "other-tenant");
        otherRecord.Complete(
            true,
            now.AddMinutes(-4),
            null,
            null,
            200,
            1,
            1,
            "APPROVE",
            0.9,
            true,
            false,
            null);

        db.AgentExecutionRecords.AddRange(tenantRecord, otherRecord);
        await db.SaveChangesAsync();

        var service = Service(db);
        var history = await service.GetExecutionHistoryAsync(
            new AgentExecutionHistoryRequest(
                tenantId,
                now.AddDays(-1),
                now.AddDays(1),
                instanceId));

        var execution = Assert.Single(history.Executions);
        Assert.Equal(tenantRecord.ExecutionId, execution.ExecutionId);
        Assert.Equal("EXECUTION_FAILED", execution.FailureCode);
        Assert.Equal("User:reviewer", execution.OverrideActor);
        Assert.Equal("REJECT", execution.OverrideEvent);

        var json = JsonSerializer.Serialize(history);
        Assert.DoesNotContain("sk-never-return", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("response body", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive override reason", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sanitizedFailure", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parkReason", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("idempotencyKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("claimant", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("other-tenant", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Metrics_CalculateOutcomesCalibrationLatencyTokensQuotaAndOverrides()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();

        var committed = Execution(
            tenantId,
            Guid.NewGuid(),
            now.AddMinutes(-20),
            "flowos-hosted",
            "openai",
            "gpt-test");
        committed.Complete(
            true,
            committed.StartedAtUtc.AddMilliseconds(100),
            null,
            null,
            200,
            10,
            1,
            "APPROVE",
            0.9,
            true,
            false,
            null);

        var overridden = Execution(
            tenantId,
            Guid.NewGuid(),
            now.AddMinutes(-15),
            "flowos-hosted",
            "openai",
            "gpt-test");
        overridden.Complete(
            true,
            overridden.StartedAtUtc.AddMilliseconds(200),
            null,
            null,
            200,
            20,
            2,
            "APPROVE",
            0.8,
            false,
            true,
            "Awaiting human review.");

        var quota = Execution(
            tenantId,
            Guid.NewGuid(),
            now.AddMinutes(-10),
            "flowos-hosted",
            "openai",
            "gpt-test");
        quota.Complete(
            false,
            quota.StartedAtUtc.AddMilliseconds(300),
            AgentFailureCodes.HostedQuotaDenied,
            "Hosted quota denied.",
            429,
            null,
            null,
            null,
            null,
            false,
            false,
            null);

        var unevaluated = Execution(
            tenantId,
            Guid.NewGuid(),
            now.AddMinutes(-5),
            "tenant-claude",
            "anthropic",
            "claude-test");
        unevaluated.Complete(
            true,
            unevaluated.StartedAtUtc.AddMilliseconds(400),
            null,
            null,
            200,
            30,
            3,
            "REVIEW",
            0.4,
            false,
            true,
            "Awaiting human review.");

        db.AgentExecutionRecords.AddRange(
            committed,
            overridden,
            quota,
            unevaluated);
        var humanOverride = new StandardEvent(tenantId, "REJECT");
        humanOverride.SetCorrelationId(overridden.WorkflowInstanceId);
        humanOverride.AddMetadata("FromStep", overridden.StepId);
        humanOverride.AddMetadata("ActorId", "User:reviewer");
        db.Events.Add(humanOverride);
        await db.SaveChangesAsync();

        var service = Service(db);
        var metrics = await service.GetEvaluationMetricsAsync(
            new AgentEvaluationMetricsRequest(
                tenantId,
                now.AddDays(-1),
                now.AddDays(1)));

        Assert.Equal(4, metrics.Runs);
        Assert.Equal(3, metrics.SucceededRuns);
        Assert.Equal(1, metrics.FailedRuns);
        Assert.Equal(1, metrics.Commits);
        Assert.Equal(2, metrics.Parks);
        Assert.Equal(1, metrics.HostedQuotaDenials);
        Assert.Equal(1, metrics.Overrides);
        Assert.Equal(3, metrics.Suggestions);
        Assert.Equal(2, metrics.EvaluatedOutcomes);
        Assert.Equal(1, metrics.UnevaluatedOutcomes);
        Assert.Equal(1, metrics.MatchedOutcomes);
        Assert.Equal(1, metrics.UnmatchedOutcomes);

        Assert.Equal(4, metrics.Latency.Samples);
        Assert.Equal(250, metrics.Latency.AverageMs);
        Assert.Equal(200, metrics.Latency.P50Ms);
        Assert.Equal(400, metrics.Latency.P95Ms);
        Assert.Equal(60, metrics.Tokens.InputTokens);
        Assert.Equal(6, metrics.Tokens.OutputTokens);

        Assert.Equal(2, metrics.ConfidenceCalibration.Samples);
        Assert.Equal(0.325, metrics.ConfidenceCalibration.BrierScore!.Value, 6);
        var highConfidence = Assert.Single(
            metrics.ConfidenceCalibration.Bins,
            bin => bin.Label == "[0.8,1.0]");
        Assert.Equal(2, highConfidence.Samples);
        Assert.Equal(1, highConfidence.MatchedOutcomes);
        Assert.Equal(0.85, highConfidence.MeanConfidence!.Value, 6);
        Assert.Equal(0.5, highConfidence.ObservedMatchRate!.Value, 6);

        var breakdown = Assert.Single(
            metrics.ProviderModelBreakdown,
            item => item.ProviderAlias == "flowos-hosted");
        Assert.Equal(3, breakdown.Runs);
        Assert.Equal(1, breakdown.HostedQuotaDenials);
        Assert.Equal(1, breakdown.Overrides);

        db.ChangeTracker.Clear();
        var unchanged = await db.AgentExecutionRecords
            .SingleAsync(record => record.ExecutionId == overridden.ExecutionId);
        Assert.Null(unchanged.ObservedOutcome);
        Assert.False(unchanged.WasOverridden);
    }

    private static AgentObservabilityQueryService Service(FlowOSDbContext db) =>
        new(
            new AgentExecutionStore(db),
            new UnitOfWork(db));

    private static AgentExecutionRecord Execution(
        Guid tenantId,
        Guid workflowInstanceId,
        DateTime startedAtUtc,
        string providerAlias = "flowos-hosted",
        string providerName = "openai",
        string model = "gpt-test")
    {
        var record = new AgentExecutionRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            tenantId,
            workflowInstanceId,
            "AgentReview",
            "Agent:test",
            AgentExecutionMode.Live,
            startedAtUtc,
            "worker-test");
        record.SetProviderMetadata(
            providerAlias,
            providerName,
            model,
            "prompt-alias-only");
        record.SetVersionMetadata(
            "TenantLlmWorkflowAgent",
            "1.2.3",
            Guid.NewGuid(),
            4,
            Guid.NewGuid(),
            Guid.NewGuid(),
            8);
        record.SetTraceIdentifiers(
            "idempotency-secret-never-return",
            workflowInstanceId);
        return record;
    }

    private static FlowOSDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"agent-observability-{Guid.NewGuid():N}")
            .Options;
        return new FlowOSDbContext(options);
    }
}
