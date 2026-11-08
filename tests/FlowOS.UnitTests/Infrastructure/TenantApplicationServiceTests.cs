using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Infrastructure;

public class TenantApplicationServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static FlowOSDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"tenant-apps-{Guid.NewGuid()}")
            .Options);

    private static TenantApiKey AddKey(FlowOSDbContext db, TenantApplication app, bool revoked = false)
    {
        var key = new TenantApiKey(TenantId, "Billing backend", TenantApiKey.GenerateRawKey());
        key.IssueForApplication(app);
        if (revoked)
            key.Revoke();
        db.TenantApiKeys.Add(key);
        return key;
    }

    [Fact]
    public async Task Create_then_list_returns_the_application()
    {
        await using var db = NewDb();
        var service = new TenantApplicationService(db);

        var created = await service.CreateAsync(TenantId, "  Finance Approvals ", "Expense and loan approvals");

        Assert.Equal("Finance Approvals", created.Name);
        var listed = Assert.Single(await service.ListAsync(TenantId));
        Assert.Equal(created.Id, listed.Id);
        Assert.Empty(await service.ListAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_rejects_a_name_already_used_in_the_tenant_ignoring_case()
    {
        await using var db = NewDb();
        var service = new TenantApplicationService(db);
        await service.CreateAsync(TenantId, "Commerce", null);

        var ex = await Assert.ThrowsAsync<TenantApplicationException>(
            () => service.CreateAsync(TenantId, "commerce", null));

        Assert.Equal(TenantApplicationErrorCodes.NameTaken, ex.Code);
    }

    [Fact]
    public async Task Create_rejects_a_blank_name_with_a_typed_code()
    {
        await using var db = NewDb();
        var service = new TenantApplicationService(db);

        var ex = await Assert.ThrowsAsync<TenantApplicationException>(
            () => service.CreateAsync(TenantId, "   ", null));

        Assert.Equal(TenantApplicationErrorCodes.Invalid, ex.Code);
    }

    [Fact]
    public async Task Rename_relabels_the_applications_keys_and_counts_only_active_keys()
    {
        await using var db = NewDb();
        var service = new TenantApplicationService(db);
        var dto = await service.CreateAsync(TenantId, "Ops", null);
        var app = await db.TenantApplications.SingleAsync(a => a.Id == dto.Id);
        var active = AddKey(db, app);
        AddKey(db, app, revoked: true);
        await db.SaveChangesAsync();

        var renamed = await service.UpdateAsync(TenantId, dto.Id, "Ops Incidents", "Escalations");

        Assert.Equal("Ops Incidents", renamed.Name);
        Assert.Equal(1, renamed.ActiveKeyCount);
        Assert.All(await db.TenantApiKeys.ToListAsync(), k => Assert.Equal("Ops Incidents", k.ApplicationName));
        Assert.Equal(dto.Id, active.ApplicationId);
    }

    [Fact]
    public async Task Delete_is_refused_while_an_active_key_exists()
    {
        await using var db = NewDb();
        var service = new TenantApplicationService(db);
        var dto = await service.CreateAsync(TenantId, "Commerce", null);
        AddKey(db, await db.TenantApplications.SingleAsync(a => a.Id == dto.Id));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<TenantApplicationException>(
            () => service.DeleteAsync(TenantId, dto.Id));

        Assert.Equal(TenantApplicationErrorCodes.InUse, ex.Code);
        Assert.NotNull(await service.GetAsync(TenantId, dto.Id));
    }

    [Fact]
    public async Task Delete_unlinks_revoked_keys_and_removes_the_application()
    {
        await using var db = NewDb();
        var service = new TenantApplicationService(db);
        var dto = await service.CreateAsync(TenantId, "Commerce", null);
        var revoked = AddKey(db, await db.TenantApplications.SingleAsync(a => a.Id == dto.Id), revoked: true);
        await db.SaveChangesAsync();

        await service.DeleteAsync(TenantId, dto.Id);

        Assert.Null(await service.GetAsync(TenantId, dto.Id));
        Assert.Null(revoked.ApplicationId);
    }

    [Fact]
    public async Task Another_tenants_application_is_not_found()
    {
        await using var db = NewDb();
        var service = new TenantApplicationService(db);
        var dto = await service.CreateAsync(TenantId, "Commerce", null);

        Assert.Null(await service.GetAsync(Guid.NewGuid(), dto.Id));
        var ex = await Assert.ThrowsAsync<TenantApplicationException>(
            () => service.UpdateAsync(Guid.NewGuid(), dto.Id, "Hijack", null));
        Assert.Equal(TenantApplicationErrorCodes.NotFound, ex.Code);
    }

    [Fact]
    public void IssueForApplication_links_the_key_and_marks_it_restricted()
    {
        var app = new TenantApplication(TenantId, "Finance Approvals", null, DateTime.UtcNow);
        var key = new TenantApiKey(TenantId, "Mobile app", TenantApiKey.GenerateRawKey(), "free text label");

        Assert.False(key.IsApplicationRestricted);
        key.IssueForApplication(app);

        Assert.Equal(app.Id, key.ApplicationId);
        Assert.Equal("Finance Approvals", key.ApplicationName);
        Assert.True(key.IsApplicationRestricted);
    }

    [Fact]
    public void IssueForApplication_rejects_another_tenants_application()
    {
        var app = new TenantApplication(Guid.NewGuid(), "Elsewhere", null, DateTime.UtcNow);
        var key = new TenantApiKey(TenantId, "Mobile app", TenantApiKey.GenerateRawKey());

        Assert.Throws<ArgumentException>(() => key.IssueForApplication(app));
        Assert.Null(key.ApplicationId);
    }
}
