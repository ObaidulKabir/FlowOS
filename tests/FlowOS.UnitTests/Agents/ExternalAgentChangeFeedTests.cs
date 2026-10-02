using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities.ExternalAI;
using FlowOS.Domain.Enums;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.UnitTests.Agents;

public sealed class ExternalAgentChangeFeedTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string ReadError(CallToolResult r)
    {
        if (!r.IsError || r.Content.Count == 0) return "no-error-content";
        try { return JObject.Parse(r.Content[0].Text ?? "{}").Value<string>("message") ?? "empty-message"; }
        catch { return "unparseable-error"; }
    }

    private static ExternalAgentChangeRecord CreatePendingRecord(Guid? id = null, Guid? tenantId = null, int maxAttempts = 5)
    {
        return ExternalAgentChangeRecord.Create(
            id ?? Guid.NewGuid(),
            tenantId ?? TenantId,
            Guid.NewGuid(),
            "EVT-APPROVED",
            """{"amount":4800,"status":"ok"}""",
            DateTime.UtcNow,
            maxAttempts);
    }

    [Fact]
    public async Task TR4a_Two_Polls_Return_Disjoint_Record_Sets_With_Different_CallerIds()
    {
        var storeMock = new Mock<IExternalAgentChangeStore>();
        var allRecords = Enumerable.Range(0, 5).Select(_ => CreatePendingRecord()).ToList();
        var leasedCallerIds = new List<string>();
        var alreadyLeasedCount = 0;
        var lockObj = new object();

        storeMock.Setup(s => s.LeaseNextAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, string, TimeSpan, int, CancellationToken>((tid, caller, ttl, limit, ct) =>
            {
                List<ExternalAgentChangeRecord> batch;
                lock (lockObj)
                {
                    leasedCallerIds.Add(caller);
                    var toTake = Math.Min(limit, Math.Max(0, allRecords.Count - alreadyLeasedCount));
                    batch = allRecords.Skip(alreadyLeasedCount).Take(toTake).ToList();
                    alreadyLeasedCount += batch.Count;
                    foreach (var r in batch)
                    {
                        r.MarkLeased(caller, ttl);
                    }
                }
                return Task.FromResult<IReadOnlyList<ExternalAgentChangeRecord>>(batch);
            });

        storeMock.Setup(s => s.ListPendingAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(allRecords.ToList());

        var tools = new ExternalAgentChangeFeedMcpTools(storeMock.Object, null);

        var argsA = JObject.Parse($$"""
            {
              "tenantId": "{{TenantId}}",
              "limit": 3,
              "peekOnly": false,
              "ttlSeconds": 120
            }
            """);
        var argsB = JObject.Parse($$"""
            {
              "tenantId": "{{TenantId}}",
              "limit": 3,
              "peekOnly": false,
              "ttlSeconds": 120
            }
            """);

        var pollA = await tools.PollExternalAgentChanges(argsA);
        var pollB = await tools.PollExternalAgentChanges(argsB);

        Assert.False(pollA.IsError, "Poll A should succeed.");
        Assert.False(pollB.IsError, "Poll B should succeed.");

        Assert.Equal(allRecords.Count, alreadyLeasedCount);
        Assert.Equal(2, leasedCallerIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(leasedCallerIds, c => Assert.StartsWith($"mcp:{TenantId}:", c, StringComparison.Ordinal));
        Assert.All(allRecords, r => Assert.Equal(ExternalAgentChangeStatus.Leased, r.Status));

        var firstCallerSuffix = leasedCallerIds[0].Substring($"mcp:{TenantId}:".Length);
        var secondCallerSuffix = leasedCallerIds[1].Substring($"mcp:{TenantId}:".Length);
        Assert.True(Guid.TryParseExact(firstCallerSuffix, "N", out _), "First caller suffix must be Guid N format.");
        Assert.True(Guid.TryParseExact(secondCallerSuffix, "N", out _), "Second caller suffix must be Guid N format.");
    }

    [Fact]
    public async Task TR4b_PeekOnly_Poll_Does_Not_Lease_Or_Mutate_State()
    {
        var original = CreatePendingRecord();
        var snapshotStatus = original.Status;
        var snapshotLeasedBy = original.LeasedByAgent;
        var snapshotAttempts = original.AttemptCount;

        var storeMock = new Mock<IExternalAgentChangeStore>();
        storeMock.Setup(s => s.ListPendingAsync(TenantId, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalAgentChangeRecord> { original });

        var tools = new ExternalAgentChangeFeedMcpTools(storeMock.Object);
        var args = JObject.FromObject(new
        {
            tenantId = TenantId.ToString(),
            peekOnly = true
        });

        var result = await tools.PollExternalAgentChanges(args);

        Assert.False(result.IsError);
        storeMock.Verify(s => s.LeaseNextAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(snapshotStatus, original.Status);
        Assert.Equal(snapshotLeasedBy, original.LeasedByAgent);
        Assert.Equal(snapshotAttempts, original.AttemptCount);
    }

    [Fact]
    public async Task TR4c_NonPeek_Poll_Leases_With_Mcp_CallerId_And_Ttl()
    {
        var record = CreatePendingRecord();
        var storeMock = new Mock<IExternalAgentChangeStore>();
        string? capturedCaller = null;
        TimeSpan capturedTtl = TimeSpan.Zero;
        int capturedLimit = 0;

        storeMock.Setup(s => s.LeaseNextAsync(
                TenantId,
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                5,
                It.IsAny<CancellationToken>()))
            .Returns<Guid, string, TimeSpan, int, CancellationToken>((tid, caller, ttl, limit, ct) =>
            {
                capturedCaller = caller;
                capturedTtl = ttl;
                capturedLimit = limit;
                record.MarkLeased(caller, ttl);
                return Task.FromResult<IReadOnlyList<ExternalAgentChangeRecord>>(new List<ExternalAgentChangeRecord> { record });
            });

        var tools = new ExternalAgentChangeFeedMcpTools(storeMock.Object);
        var args = JObject.FromObject(new
        {
            tenantId = TenantId.ToString(),
            limit = 5,
            ttlSeconds = 180,
            peekOnly = false
        });

        var result = await tools.PollExternalAgentChanges(args);

        Assert.False(result.IsError);
        Assert.NotNull(capturedCaller);
        Assert.StartsWith($"mcp:{TenantId}:", capturedCaller, StringComparison.Ordinal);
        var suffix = capturedCaller.Substring($"mcp:{TenantId}:".Length);
        Assert.True(Guid.TryParseExact(suffix, "N", out _), "Caller suffix must be a Guid N formatted string.");
        Assert.Equal(TimeSpan.FromSeconds(180), capturedTtl);
        Assert.Equal(5, capturedLimit);
        Assert.Equal(ExternalAgentChangeStatus.Leased, record.Status);
        Assert.Equal(capturedCaller, record.LeasedByAgent);
    }

    [Fact]
    public async Task TR4d_Ack_Idempotent_Succeeded_Marks_Processed_Failed_Handles_Retry_And_DeadLetter()
    {
        var recordSucceededId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var recordSucceeded = ExternalAgentChangeRecord.Create(
            recordSucceededId, TenantId, Guid.NewGuid(), "EVT-A", "{}", DateTime.UtcNow);
        recordSucceeded.MarkLeased("mcp:agent-1", TimeSpan.FromMinutes(2));

        var recordFailedRetryId = Guid.NewGuid();
        var recordFailedRetry = ExternalAgentChangeRecord.Create(
            recordFailedRetryId, TenantId, Guid.NewGuid(), "EVT-B", "{}", DateTime.UtcNow, maxAttempts: 3);
        recordFailedRetry.MarkLeased("mcp:agent-1", TimeSpan.FromMinutes(2));

        var deadLetterId = Guid.NewGuid();
        var recordDeadLetter = ExternalAgentChangeRecord.Create(
            deadLetterId, TenantId, Guid.NewGuid(), "EVT-C", "{}", DateTime.UtcNow, maxAttempts: 1);
        recordDeadLetter.MarkLeased("mcp:agent-1", TimeSpan.FromMinutes(2));

        var storeMock = new Mock<IExternalAgentChangeStore>();
        var recordsById = new Dictionary<Guid, ExternalAgentChangeRecord>
        {
            [recordSucceededId] = recordSucceeded,
            [recordFailedRetryId] = recordFailedRetry,
            [deadLetterId] = recordDeadLetter
        };

        storeMock.Setup(s => s.AckAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, bool, string?, CancellationToken>((cid, succeeded, err, ct) =>
            {
                if (!recordsById.TryGetValue(cid, out var r)) return Task.CompletedTask;
                if (succeeded)
                {
                    if (r.Status != ExternalAgentChangeStatus.Processed)
                        r.MarkProcessed();
                }
                else
                {
                    if (r.Status != ExternalAgentChangeStatus.DeadLetter)
                        r.MarkFailed(err ?? "Unknown error", DateTime.UtcNow);
                }
                return Task.CompletedTask;
            });

        storeMock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((cid, ct) =>
                Task.FromResult(recordsById.TryGetValue(cid, out var r) ? r : null));

        var tools = new ExternalAgentChangeFeedMcpTools(storeMock.Object);

        var ackSucceeded = await tools.AckExternalAgentChange(JObject.FromObject(new
        {
            changeId = recordSucceededId,
            result = "succeeded"
        }));
        Assert.False(ackSucceeded.IsError, ReadError(ackSucceeded));
        Assert.Equal(ExternalAgentChangeStatus.Processed, recordSucceeded.Status);
        Assert.Null(recordSucceeded.LeasedByAgent);
        Assert.NotNull(recordSucceeded.ProcessedAtUtc);

        var ackSucceededAgain = await tools.AckExternalAgentChange(JObject.FromObject(new
        {
            changeId = recordSucceededId,
            result = "succeeded"
        }));
        Assert.False(ackSucceededAgain.IsError, $"Second succeeded ack should be idempotent: {ReadError(ackSucceededAgain)}");

        var ackFailed = await tools.AckExternalAgentChange(JObject.FromObject(new
        {
            changeId = recordFailedRetryId,
            result = "failed",
            error = "downstream 500"
        }));
        Assert.False(ackFailed.IsError, ReadError(ackFailed));
        Assert.Equal(ExternalAgentChangeStatus.Failed, recordFailedRetry.Status);
        Assert.Equal(1, recordFailedRetry.AttemptCount);
        Assert.Equal("downstream 500", recordFailedRetry.LastError);
        Assert.NotNull(recordFailedRetry.NextRetryUtc);

        var ackDead = await tools.AckExternalAgentChange(JObject.FromObject(new
        {
            changeId = deadLetterId,
            result = "failed",
            error = "permanent"
        }));
        Assert.False(ackDead.IsError, ReadError(ackDead));
        Assert.Equal(ExternalAgentChangeStatus.DeadLetter, recordDeadLetter.Status);
        Assert.Null(recordDeadLetter.LeasedByAgent);
    }

    [Fact]
    public async Task TR4e_RenewLease_Works_Only_For_Leased_Records_With_Stored_Agent_Identity()
    {
        var pendingId = Guid.NewGuid();
        var pendingRecord = ExternalAgentChangeRecord.Create(
            pendingId, TenantId, Guid.NewGuid(), "EVT-E", "{}", DateTime.UtcNow);

        var leasedId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var leasedRecord = ExternalAgentChangeRecord.Create(
            leasedId, TenantId, Guid.NewGuid(), "EVT-D", "{}", DateTime.UtcNow);
        leasedRecord.MarkLeased("mcp:agent-renewer", TimeSpan.FromSeconds(60));
        var originalLeasedUntil = leasedRecord.LeasedUntilUtc!.Value;

        var storeMock = new Mock<IExternalAgentChangeStore>();
        var recordsById = new Dictionary<Guid, ExternalAgentChangeRecord>
        {
            [leasedId] = leasedRecord,
            [pendingId] = pendingRecord
        };
        string? renewCaller = null;
        TimeSpan renewTtl = TimeSpan.Zero;
        int renewHits = 0;

        storeMock.Setup(s => s.RenewLeaseAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, TimeSpan, CancellationToken>((cid, agentId, ttl, ct) =>
            {
                if (!recordsById.TryGetValue(cid, out var r)) return Task.CompletedTask;
                if (r.Status != ExternalAgentChangeStatus.Leased) return Task.CompletedTask;
                if (r.LeasedByAgent != agentId) return Task.CompletedTask;
                renewHits++;
                renewCaller = agentId;
                renewTtl = ttl;
                r.RenewLease(ttl);
                return Task.CompletedTask;
            });

        storeMock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((cid, ct) =>
                Task.FromResult(recordsById.TryGetValue(cid, out var r) ? r : null));

        var tools = new ExternalAgentChangeFeedMcpTools(storeMock.Object);

        var renewPending = await tools.RenewChangeLease(JObject.FromObject(new
        {
            changeId = pendingId,
            ttlSeconds = 240
        }));
        Assert.True(renewPending.IsError);

        var renewArgs = JObject.FromObject(new
        {
            changeId = leasedId,
            ttlSeconds = 240
        });

        var renewLeased = await tools.RenewChangeLease(renewArgs);
        Assert.False(renewLeased.IsError, $"Renew should succeed for Leased record: {ReadError(renewLeased)}");
        Assert.Equal(1, renewHits);
        Assert.Equal("mcp:agent-renewer", renewCaller);
        Assert.Equal(TimeSpan.FromSeconds(240), renewTtl);
        Assert.True(leasedRecord.LeasedUntilUtc > originalLeasedUntil);
    }

    [Fact]
    public async Task TR4f_Get_Returns_Joined_Plan_And_Full_Record()
    {
        var changeId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var outboxId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var record = ExternalAgentChangeRecord.FromOutboxMessage(
            changeId,
            TenantId,
            outboxId,
            "EVT-JOINED",
            """{"hello":"world"}""",
            DateTime.UtcNow);

        var planId = Guid.NewGuid();
        var step1 = ExternalAgentPlanStepRecord.Create(
            1L, planId, "step-validate", 0, "validate_schema", "{}",
            "Validate input", "[]");
        var step2 = ExternalAgentPlanStepRecord.Create(
            2L, planId, "step-enrich", 1, "enrich_context", "{}",
            "Enrich data", "[\"step-validate\"]");
        var steps = new List<ExternalAgentPlanStepRecord> { step1, step2 };

        var planRecord = ExternalAgentPlanRecord.Create(planId, changeId, TenantId, "ops-profile", DateTime.UtcNow, "[]", 1);
        var planStoreMock = new Mock<IExternalAgentPlanStore>();
        planStoreMock.Setup(s => s.GetLatestByChangeAsync(changeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planRecord);
        planStoreMock.Setup(s => s.GetPlanStepsAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(steps);

        var storeMock = new Mock<IExternalAgentChangeStore>();
        storeMock.Setup(s => s.GetByIdAsync(changeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var tools = new ExternalAgentChangeFeedMcpTools(storeMock.Object, planStoreMock.Object);
        var result = await tools.GetExternalAgentChange(JObject.FromObject(new { changeId }));

        Assert.False(result.IsError, ReadError(result));
        var contentText = Assert.Single(result.Content);
        var json = JObject.Parse(contentText.Text!);
        Assert.True(json.Value<bool>("ok"));
        var data = json["data"] as JObject;
        Assert.NotNull(data);

        var idStr = data?.Value<string>("id");
        Assert.NotNull(idStr);
        Assert.Equal(changeId, Guid.Parse(idStr));

        var sourceStr = data?.Value<string>("sourceOutboxMessageId");
        Assert.NotNull(sourceStr);
        Assert.Equal(outboxId, Guid.Parse(sourceStr));

        Assert.Equal("EVT-JOINED", data?.Value<string>("eventType"));

        var associatedPlan = data?["associatedPlan"] as JObject;
        Assert.NotNull(associatedPlan);

        var planIdStr = associatedPlan?.Value<string>("planId");
        Assert.NotNull(planIdStr);
        Assert.Equal(planId, Guid.Parse(planIdStr));

        Assert.Equal(2, associatedPlan?.Value<int?>("planStepCount"));
        var planStepsArr = associatedPlan?["planSteps"] as JArray;
        Assert.NotNull(planStepsArr);
        Assert.Equal(2, planStepsArr.Count);

        var step1J = planStepsArr[0] as JObject;
        Assert.NotNull(step1J);
        Assert.Equal("step-validate", step1J.Value<string>("stepId"));
        Assert.Equal("validate_schema", step1J.Value<string>("toolName"));
        Assert.Equal(0, step1J.Value<int>("stepIndex"));
    }

    [Fact]
    public async Task Tool_Argument_Validation_Covers_Required_And_Ranges()
    {
        var storeMock = new Mock<IExternalAgentChangeStore>();
        storeMock.Setup(s => s.ListAsync(TenantId, It.IsAny<ExternalAgentChangeStatus?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalAgentChangeRecord>());
        var tools = new ExternalAgentChangeFeedMcpTools(storeMock.Object);

        var badAckNoChangeId = await tools.AckExternalAgentChange(JObject.FromObject(new { result = "succeeded" }));
        Assert.True(badAckNoChangeId.IsError);

        var badAckBadResult = await tools.AckExternalAgentChange(JObject.FromObject(new
        {
            changeId = Guid.NewGuid(),
            result = "weird"
        }));
        Assert.True(badAckBadResult.IsError);

        var missingChangeId = await tools.GetExternalAgentChange(new JObject());
        Assert.True(missingChangeId.IsError);

        var badLeaseNoId = await tools.RenewChangeLease(JObject.FromObject(new { ttlSeconds = 120 }));
        Assert.True(badLeaseNoId.IsError);

        McpRequestContext.TenantId = Guid.Empty;
        McpRequestContext.IsAuthenticatedTransport = false;
        var tenantArgs = JObject.Parse($$"""{"tenantId": "{{TenantId}}"}""");
        var listResult = await tools.ListExternalAgentChanges(tenantArgs);
        Assert.False(listResult.IsError, ReadError(listResult));
    }
}
