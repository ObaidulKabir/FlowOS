using FlowOS.Application.Billing;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FlowOS.UnitTests.Billing;

public class TenantPlanActivationServiceTests
{
    [Fact]
    public async Task Submit_StoresPendingRequest_AndDoesNotActivateThePlan()
    {
        using var h = CreateHarness();
        var tenant = await SeedAsync(h.Db);

        var result = await h.Service.SubmitAsync(
            tenant.TenantId, new PlanActivationInput(" team ", " TRX-1 ", "a@b.test", " note "), idempotent: true);

        Assert.Equal(PlanActivationOutcome.Submitted, result.Outcome);
        Assert.Equal("Team", result.PlanRequest!.RequestedPlan);
        Assert.Equal("TRX-1", result.PlanRequest.PaymentReference);
        Assert.Equal("Pending", result.PlanRequest.Status);

        var stored = await h.Db.Tenants.FindAsync(tenant.TenantId);
        Assert.False(stored!.CanRunRuntime);
        Assert.Equal("Pending", stored.GetPlanRequest()!.Status);
    }

    [Fact]
    public async Task Submit_Idempotent_ReturnsAlreadyPending_ForSamePlanAndReference()
    {
        using var h = CreateHarness();
        var tenant = await SeedAsync(h.Db);
        var input = new PlanActivationInput("Team", "TRX-1", null, null);

        await h.Service.SubmitAsync(tenant.TenantId, input, idempotent: true);
        var second = await h.Service.SubmitAsync(tenant.TenantId, input, idempotent: true);

        Assert.Equal(PlanActivationOutcome.AlreadyPending, second.Outcome);
    }

    [Fact]
    public async Task Submit_Idempotent_FilesNewRequest_WhenReferenceChanges()
    {
        using var h = CreateHarness();
        var tenant = await SeedAsync(h.Db);

        await h.Service.SubmitAsync(tenant.TenantId, new PlanActivationInput("Team", "TRX-1", null, null), idempotent: true);
        var second = await h.Service.SubmitAsync(tenant.TenantId, new PlanActivationInput("Team", "TRX-2", null, null), idempotent: true);

        Assert.Equal(PlanActivationOutcome.Submitted, second.Outcome);
        Assert.Equal("TRX-2", second.PlanRequest!.PaymentReference);
    }

    [Fact]
    public async Task Submit_NonIdempotent_AlwaysOverwritesLikeTheRestEndpoint()
    {
        using var h = CreateHarness();
        var tenant = await SeedAsync(h.Db);
        var input = new PlanActivationInput("Team", "TRX-1", null, null);

        await h.Service.SubmitAsync(tenant.TenantId, input, idempotent: false);
        var second = await h.Service.SubmitAsync(tenant.TenantId, input, idempotent: false);

        Assert.Equal(PlanActivationOutcome.Submitted, second.Outcome);
    }

    [Fact]
    public async Task Submit_Idempotent_ReturnsAlreadyActive_ForPaidTenant()
    {
        using var h = CreateHarness();
        var tenant = await SeedAsync(h.Db);
        tenant.AssignPlan(TenantPlan.Managed, TenantBillingStatus.Active);
        await h.Db.SaveChangesAsync();

        var result = await h.Service.SubmitAsync(
            tenant.TenantId, new PlanActivationInput("Team", null, null, null), idempotent: true);

        Assert.Equal(PlanActivationOutcome.AlreadyActive, result.Outcome);
        Assert.Null((await h.Db.Tenants.FindAsync(tenant.TenantId))!.GetPlanRequest());
    }

    [Fact]
    public async Task Submit_RejectsUnknownPlan_AndUnknownTenant()
    {
        using var h = CreateHarness();
        var tenant = await SeedAsync(h.Db);

        var bad = await h.Service.SubmitAsync(
            tenant.TenantId, new PlanActivationInput("Platinum", null, null, null), idempotent: true);
        var missing = await h.Service.SubmitAsync(
            Guid.NewGuid(), new PlanActivationInput("Team", null, null, null), idempotent: true);

        Assert.Equal(PlanActivationOutcome.InvalidPlan, bad.Outcome);
        Assert.Equal(PlanActivationOutcome.TenantNotFound, missing.Outcome);
    }

    [Fact]
    public async Task GetStatus_ReportsPlanBillingAndRequest()
    {
        using var h = CreateHarness();
        var tenant = await SeedAsync(h.Db);
        await h.Service.SubmitAsync(tenant.TenantId, new PlanActivationInput("Scale", null, null, null), idempotent: true);

        var status = await h.Service.GetStatusAsync(tenant.TenantId);

        Assert.NotNull(status);
        Assert.False(status!.CanRunRuntime);
        Assert.True(status.BillingEnforced);
        Assert.Equal("Scale", status.PlanRequest!.RequestedPlan);
        Assert.Null(await h.Service.GetStatusAsync(Guid.NewGuid()));
    }

    private static async Task<Tenant> SeedAsync(FlowOSDbContext db)
    {
        var tenant = new Tenant("Activation Org");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static Harness CreateHarness()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new FlowOSDbContext(options);
        var entitlements = new Mock<ITenantEntitlementService>();
        entitlements.SetupGet(e => e.IsEnforced).Returns(true);
        return new Harness(db, new TenantPlanActivationService(db, entitlements.Object, TimeProvider.System));
    }

    private sealed class Harness : IDisposable
    {
        public Harness(FlowOSDbContext db, TenantPlanActivationService service)
        {
            Db = db;
            Service = service;
        }

        public FlowOSDbContext Db { get; }
        public TenantPlanActivationService Service { get; }
        public void Dispose() => Db.Dispose();
    }
}
