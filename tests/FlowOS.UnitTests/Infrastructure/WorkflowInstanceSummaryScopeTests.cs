using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Infrastructure;

public class WorkflowInstanceSummaryScopeTests
{
    private static FlowOSDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(WorkflowInstanceRepository Repo, Guid TenantA, Guid TenantB)> SeedAsync(FlowOSDbContext db)
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        db.WorkflowInstances.Add(new WorkflowInstance(tenantA, Guid.NewGuid(), Guid.NewGuid(), 1, "Start"));
        db.WorkflowInstances.Add(new WorkflowInstance(tenantA, Guid.NewGuid(), Guid.NewGuid(), 1, "Start"));
        db.WorkflowInstances.Add(new WorkflowInstance(tenantB, Guid.NewGuid(), Guid.NewGuid(), 1, "Start"));
        await db.SaveChangesAsync();
        return (new WorkflowInstanceRepository(db), tenantA, tenantB);
    }

    [Fact]
    public async Task Summaries_ForOneTenant_AreScopedAndCarryTheTenantId()
    {
        using var db = CreateDb();
        var (repo, tenantA, _) = await SeedAsync(db);

        var list = await repo.GetSummariesByTenantAsync(tenantA, null);

        Assert.Equal(2, list.Count);
        Assert.All(list, s => Assert.Equal(tenantA, s.TenantId));
    }

    [Fact]
    public async Task Summaries_WithEmptyTenant_CoverEveryTenant_AndTakeCapsTheNewest()
    {
        using var db = CreateDb();
        var (repo, tenantA, tenantB) = await SeedAsync(db);

        var all = await repo.GetSummariesByTenantAsync(Guid.Empty, null);
        var capped = await repo.GetSummariesByTenantAsync(Guid.Empty, null, take: 2);

        Assert.Equal(3, all.Count);
        Assert.Contains(all, s => s.TenantId == tenantA);
        Assert.Contains(all, s => s.TenantId == tenantB);
        Assert.Equal(2, capped.Count);
    }
}
