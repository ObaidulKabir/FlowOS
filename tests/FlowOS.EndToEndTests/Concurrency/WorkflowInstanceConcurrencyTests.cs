using System;
using System.Threading.Tasks;
using FlowOS.Application.Common.Exceptions;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.EndToEndTests.Concurrency;

/// <summary>
/// CR-06: two writers that load the same workflow instance must not overwrite each other.
/// Runs against real PostgreSQL (Testcontainers) because the token is the xmin system column.
/// </summary>
[Trait("Category", "Concurrency")]
public sealed class WorkflowInstanceConcurrencyTests : IClassFixture<PostgresConcurrencyFixture>
{
    private readonly PostgresConcurrencyFixture _fixture;

    public WorkflowInstanceConcurrencyTests(PostgresConcurrencyFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Second_writer_of_a_stale_instance_is_rejected()
    {
        var instanceId = await SeedInstanceAsync();

        await using var ctxA = _fixture.CreateContext();
        await using var ctxB = _fixture.CreateContext();
        var instanceA = await ctxA.WorkflowInstances.SingleAsync(w => w.Id == instanceId);
        var instanceB = await ctxB.WorkflowInstances.SingleAsync(w => w.Id == instanceId);

        instanceA.AdvanceTo("Approved");
        await ctxA.SaveChangesAsync();

        instanceB.AdvanceTo("Rejected");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());

        await using var check = _fixture.CreateContext();
        var stored = await check.WorkflowInstances.AsNoTracking().SingleAsync(w => w.Id == instanceId);
        Assert.Equal("Approved", stored.CurrentStepId); // the first write survives; nothing is overwritten
    }

    [Fact]
    public async Task UnitOfWork_reports_the_race_as_a_conflict_and_resets_its_changes()
    {
        var instanceId = await SeedInstanceAsync();

        await using var ctxA = _fixture.CreateContext();
        await using var ctxB = _fixture.CreateContext();
        var unitOfWorkB = new UnitOfWork(ctxB);
        var instanceA = await ctxA.WorkflowInstances.SingleAsync(w => w.Id == instanceId);
        var instanceB = await ctxB.WorkflowInstances.SingleAsync(w => w.Id == instanceId);

        instanceA.AdvanceTo("Approved");
        await ctxA.SaveChangesAsync();

        instanceB.AdvanceTo("Rejected");
        var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => unitOfWorkB.SaveChangesAsync());

        Assert.Equal(ConcurrencyConflictException.ConcurrentUpdate, conflict.Code);
        Assert.Empty(ctxB.ChangeTracker.Entries());
        // A later save in the same scope (for example an idempotency status) does not repeat the rejected change.
        Assert.Equal(0, await unitOfWorkB.SaveChangesAsync());
    }

    [Fact]
    public async Task Sequential_writers_each_see_the_latest_version()
    {
        var instanceId = await SeedInstanceAsync();

        await using (var ctx = _fixture.CreateContext())
        {
            var instance = await ctx.WorkflowInstances.SingleAsync(w => w.Id == instanceId);
            instance.AdvanceTo("Review");
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateContext())
        {
            var instance = await ctx.WorkflowInstances.SingleAsync(w => w.Id == instanceId);
            instance.AdvanceTo("Approved");
            await ctx.SaveChangesAsync();
            // The token is refreshed after each save, so the same context can save again.
            instance.AdvanceTo("Archived");
            await ctx.SaveChangesAsync();
        }

        await using var check = _fixture.CreateContext();
        var stored = await check.WorkflowInstances.AsNoTracking().SingleAsync(w => w.Id == instanceId);
        Assert.Equal("Archived", stored.CurrentStepId);
    }

    private async Task<Guid> SeedInstanceAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var instance = new WorkflowInstance(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 1, "Start");
        ctx.WorkflowInstances.Add(instance);
        await ctx.SaveChangesAsync();
        return instance.Id;
    }
}
