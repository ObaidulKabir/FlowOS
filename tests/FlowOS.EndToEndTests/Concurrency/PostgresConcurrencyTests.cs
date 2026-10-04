using System;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace FlowOS.EndToEndTests.Concurrency;

/// <summary>
/// PostgreSQL concurrency tests using Testcontainers.
/// Tests verify that the real Npgsql code paths (INSERT ... ON CONFLICT, optimistic concurrency tokens)
/// work correctly under concurrent access patterns that in-memory EF cannot replicate.
///
/// IMPORTANT: These tests require Docker to be running on the host machine.
/// If Docker is unavailable they will fail with a connection error.
/// </summary>
[Trait("Category", "Concurrency")]
public sealed class PostgresConcurrencyTests : IClassFixture<PostgresConcurrencyFixture>
{
    private readonly PostgresConcurrencyFixture _fixture;

    public PostgresConcurrencyTests(PostgresConcurrencyFixture fixture)
    {
        _fixture = fixture;
    }

    // ─── Distributed Lease ──────────────────────────────────────────────────────

    /// <summary>
    /// Two concurrent workers race to acquire the same lease.
    /// Exactly one must win and the other must get null.
    /// </summary>
    [Fact]
    public async Task DistributedLease_ConcurrentWorkers_ExactlyOneAcquires()
    {
        const string key = "test-lease-concurrent";
        const string owner1 = "worker-A";
        const string owner2 = "worker-B";

        await using var ctx1 = _fixture.CreateContext();
        await using var ctx2 = _fixture.CreateContext();
        var svc1 = new DistributedLeaseService(ctx1);
        var svc2 = new DistributedLeaseService(ctx2);

        var task1 = svc1.TryAcquireAsync(key, owner1, TimeSpan.FromMinutes(1));
        var task2 = svc2.TryAcquireAsync(key, owner2, TimeSpan.FromMinutes(1));

        var results = await Task.WhenAll(task1, task2);

        // Exactly one should win
        var winners = results.Where(r => r != null).ToList();
        Assert.Single(winners);

        var loser = results.FirstOrDefault(r => r == null);
        Assert.Null(loser);
    }

    /// <summary>
    /// After acquiring, the same owner should be able to release successfully.
    /// </summary>
    [Fact]
    public async Task DistributedLease_AcquireAndRelease_RoundTrip()
    {
        const string key = "test-lease-roundtrip";
        const string owner = "worker-main";

        await using var ctx = _fixture.CreateContext();
        var svc = new DistributedLeaseService(ctx);

        var handle = await svc.TryAcquireAsync(key, owner, TimeSpan.FromMinutes(1));
        Assert.NotNull(handle);
        Assert.Equal(owner, handle!.OwnerId);

        var released = await svc.ReleaseAsync(key, owner);
        Assert.True(released);

        // After release, another worker should be able to acquire
        await using var ctx2 = _fixture.CreateContext();
        var svc2 = new DistributedLeaseService(ctx2);
        var reAcquired = await svc2.TryAcquireAsync(key, "worker-new", TimeSpan.FromMinutes(1));
        Assert.NotNull(reAcquired);
    }

    /// <summary>
    /// An expired lease can be taken over by a new worker.
    /// </summary>
    [Fact]
    public async Task DistributedLease_ExpiredLease_CanBeTakenOverByNewWorker()
    {
        const string key = "test-lease-expiry";
        const string originalOwner = "worker-original";
        const string newOwner = "worker-takeover";

        await using var ctx1 = _fixture.CreateContext();
        var svc1 = new DistributedLeaseService(ctx1);

        // Acquire a very short lease
        var handle = await svc1.TryAcquireAsync(key, originalOwner, TimeSpan.FromMilliseconds(50));
        Assert.NotNull(handle);

        // Wait for it to expire
        await Task.Delay(200);

        // A new worker should now be able to take the lease
        await using var ctx2 = _fixture.CreateContext();
        var svc2 = new DistributedLeaseService(ctx2);
        var newHandle = await svc2.TryAcquireAsync(key, newOwner, TimeSpan.FromMinutes(1));
        Assert.NotNull(newHandle);
        Assert.Equal(newOwner, newHandle!.OwnerId);
    }

    // ─── WorkflowContextBinding Optimistic Concurrency ──────────────────────────

    /// <summary>
    /// Two readers load the same WorkflowContextBinding row, then both try to update it.
    /// The second update should throw a DbUpdateConcurrencyException because UpdatedAtUtc is a
    /// concurrency token and the first save already changed it.
    /// </summary>
    [Fact]
    public async Task WorkflowContextBinding_ConcurrentUpdates_ThrowsConcurrencyException()
    {
        var tenantId = Guid.NewGuid();
        var bindingId = await SeedContextBindingAsync(tenantId, "ConcurrencyTest", "Binding-CC");

        // Context A and B load the same row
        await using var ctxA = _fixture.CreateContext();
        await using var ctxB = _fixture.CreateContext();

        var bindingA = await ctxA.WorkflowContextBindings.FindAsync(bindingId);
        var bindingB = await ctxB.WorkflowContextBindings.FindAsync(bindingId);

        Assert.NotNull(bindingA);
        Assert.NotNull(bindingB);

        // Context A saves first - this bumps UpdatedAtUtc
        bindingA!.SetDraftRevision(Guid.NewGuid());
        await ctxA.SaveChangesAsync();

        // Context B's save should now fail with a concurrency exception
        // because UpdatedAtUtc in the database no longer matches the snapshot B loaded
        bindingB!.SetDraftRevision(Guid.NewGuid());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());
    }

    /// <summary>
    /// Verifies that the concurrency token is checked even when one of the updates Archives the binding.
    /// </summary>
    [Fact]
    public async Task WorkflowContextBinding_ArchiveAfterStaleRead_ThrowsConcurrencyException()
    {
        var tenantId = Guid.NewGuid();
        var bindingId = await SeedContextBindingAsync(tenantId, "ArchiveTest", "Binding-Archive");

        await using var ctxA = _fixture.CreateContext();
        await using var ctxB = _fixture.CreateContext();

        var bindingA = await ctxA.WorkflowContextBindings.FindAsync(bindingId);
        var bindingB = await ctxB.WorkflowContextBindings.FindAsync(bindingId);

        Assert.NotNull(bindingA);
        Assert.NotNull(bindingB);

        // A archives (updates UpdatedAtUtc)
        bindingA!.Archive();
        await ctxA.SaveChangesAsync();

        // B tries to set a draft revision on its stale snapshot
        bindingB!.SetDraftRevision(Guid.NewGuid());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());
    }

    // ─── WorkflowContextSnapshot Optimistic Concurrency ─────────────────────────

    /// <summary>
    /// WorkflowContextSnapshot has a ConcurrencyVersion token.
    /// Two concurrent updates on the same snapshot row must result in a concurrency exception.
    /// </summary>
    [Fact]
    public async Task WorkflowContextSnapshot_ConcurrentUpdates_ThrowsConcurrencyException()
    {
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        await SeedSnapshotAsync(tenantId, instanceId);

        // Two contexts load the same snapshot
        await using var ctxA = _fixture.CreateContext();
        await using var ctxB = _fixture.CreateContext();

        var snapshotA = await ctxA.WorkflowContextSnapshots.FindAsync(instanceId);
        var snapshotB = await ctxB.WorkflowContextSnapshots.FindAsync(instanceId);

        Assert.NotNull(snapshotA);
        Assert.NotNull(snapshotB);

        // A updates first, which increments the optimistic concurrency token.
        snapshotA!.Merge(new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["attempt"] = System.Text.Json.JsonSerializer.SerializeToElement(1)
        });
        await ctxA.SaveChangesAsync();

        // B's update should fail because ConcurrencyVersion changed
        snapshotB!.Merge(new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["attempt"] = System.Text.Json.JsonSerializer.SerializeToElement(2)
        });
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────



    private async Task<Guid> SeedContextBindingAsync(Guid tenantId, string contextType, string name)
    {
        await using var ctx = _fixture.CreateContext();
        var binding = new WorkflowContextBinding(tenantId, contextType, name);
        ctx.WorkflowContextBindings.Add(binding);
        await ctx.SaveChangesAsync();
        return binding.Id;
    }

    private async Task SeedSnapshotAsync(Guid tenantId, Guid instanceId)
    {
        // First we need a WorkflowInstance row (FK requirement)
        await using var ctx = _fixture.CreateContext();

        // Seed minimal prerequisite entities
        var revisionId = await SeedWorkflowInstanceAsync(ctx, tenantId, instanceId);
        await ctx.SaveChangesAsync();

        var snapshot = new FlowOS.Domain.Entities.WorkflowContextSnapshot(
            instanceId,
            tenantId,
            revisionId,
            new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>(
                StringComparer.OrdinalIgnoreCase));

        ctx.WorkflowContextSnapshots.Add(snapshot);
        await ctx.SaveChangesAsync();
    }

    private static async Task<Guid> SeedWorkflowInstanceAsync(
        FlowOS.Infrastructure.Persistence.FlowOSDbContext ctx,
        Guid tenantId,
        Guid instanceId)
    {
        var definitionId = Guid.NewGuid();

        var wfClass = new FlowOS.Domain.Entities.WorkflowClass(
            tenantId,
            "TestClass",
            "1.0.0",
            new FlowOS.Domain.Blueprints.WorkflowClassBlueprint());
        ctx.WorkflowClasses.Add(wfClass);
        var binding = new WorkflowContextBinding(tenantId, "SnapshotTest", "Binding-Snapshot");
        ctx.WorkflowContextBindings.Add(binding);

        await ctx.SaveChangesAsync();

        // Insert WorkflowInstance using low-level SQL to avoid domain invariants that require full graph
        var sql1 = $$"""
            INSERT INTO "WorkflowInstances" (
                "Id", "TenantId", "WorkflowDefinitionId", "WorkflowClassId", "WorkflowVersion",
                "CurrentStepId", "Status", "ActiveStepIds", "CompletedParallelStepIds",
                "PathTravelCounts", "RoleAssignments", "CreatedAt"
            ) VALUES (
                '{{instanceId}}', '{{tenantId}}', '{{definitionId}}', '{{wfClass.Id}}', 1,
                'start', 'Active', '[]', '[]',
                '{}', '{}', NOW()
            )
            """;
        await ctx.Database.ExecuteSqlRawAsync(sql1.Replace("{", "{{").Replace("}", "}}"));

        var revision = new WorkflowContextBindingRevision(
            binding.Id,
            1,
            wfClass.Id,
            wfClass.Version,
            new FlowOS.Domain.ValueObjects.WorkflowContextBindingDefinition());
        ctx.WorkflowContextBindingRevisions.Add(revision);
        await ctx.SaveChangesAsync();

        return revision.Id;
    }
}
