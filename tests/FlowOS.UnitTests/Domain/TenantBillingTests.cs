using System;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Domain;

public class TenantBillingTests
{
    [Fact]
    public void NewTenant_IsTrialUnpaid_AndCannotRunRuntime()
    {
        var tenant = new Tenant("Acme Trial Org");

        Assert.Equal(TenantPlan.Trial, tenant.Plan);
        Assert.Equal(TenantBillingStatus.Unpaid, tenant.BillingStatus);
        Assert.False(tenant.CanRunRuntime);
    }

    [Fact]
    public void AssignPlan_ManagedActive_EnablesRuntime()
    {
        var tenant = new Tenant("Acme Paid Org");

        tenant.AssignPlan(TenantPlan.Managed, TenantBillingStatus.Active);

        Assert.Equal(TenantPlan.Managed, tenant.Plan);
        Assert.Equal(TenantBillingStatus.Active, tenant.BillingStatus);
        Assert.True(tenant.CanRunRuntime);
    }

    [Fact]
    public void AssignPlan_EnterprisePastDue_CannotRunRuntime()
    {
        var tenant = new Tenant("Acme Enterprise Org");
        tenant.AssignPlan(TenantPlan.Enterprise, TenantBillingStatus.PastDue);

        Assert.False(tenant.CanRunRuntime);
    }

    [Fact]
    public void AssignPlan_None_Throws()
    {
        var tenant = new Tenant("Invalid Plan Org");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tenant.AssignPlan(TenantPlan.None, TenantBillingStatus.Active));
    }

    [Fact]
    public void EfModel_GrandfathersExistingRowsToManagedActive()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new FlowOSDbContext(options);

        var entity = db.Model.FindEntityType(typeof(Tenant));
        Assert.NotNull(entity);

        var plan = entity.FindProperty(nameof(Tenant.Plan));
        var billing = entity.FindProperty(nameof(Tenant.BillingStatus));
        Assert.NotNull(plan);
        Assert.NotNull(billing);
        Assert.Equal(TenantPlan.Managed, plan.GetDefaultValue());
        Assert.Equal(TenantBillingStatus.Active, billing.GetDefaultValue());
    }
}
