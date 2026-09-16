using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class TenantEntitlementServiceTests
{
    [Theory]
    [InlineData("simulate_workflowclass", false, "none", false)]
    [InlineData("lint_draft_workflowclass", false, "none", false)]
    [InlineData("validate_draft_workflowclass", false, "none", false)]
    [InlineData("describe_workflowclass_schema", false, "none", false)]
    [InlineData("explain_validation_violation", false, "none", false)]
    [InlineData("list_public_workflowclasses", false, "none", false)]
    [InlineData("create_draft_workflowclass", true, "reversible", false)]
    [InlineData("update_draft_workflowclass", true, "reversible", false)]
    [InlineData("create_context_binding", true, "reversible", false)]
    [InlineData("update_context_binding", true, "reversible", false)]
    [InlineData("start_workflow", true, "irreversible", true)]
    [InlineData("publish_event", true, "irreversible", true)]
    [InlineData("run_agent_task", true, "irreversible", true)]
    [InlineData("complete_task", true, "irreversible", true)]
    [InlineData("publish_workflowclass", true, "irreversible", true)]
    [InlineData("activate_context_binding", true, "irreversible", true)]
    public void McpToolRequiresPaidPlan_MatchesDesignTimeVsRuntime(
        string toolName, bool mutating, string sideEffect, bool requiresPaid)
    {
        Assert.Equal(
            requiresPaid,
            TenantEntitlementPolicy.McpToolRequiresPaidPlan(toolName, mutating, sideEffect));
    }

    [Fact]
    public void Development_DoesNotEnforce()
    {
        using var harness = CreateHarness("Development");
        Assert.False(harness.Service.IsEnforced);
    }

    [Fact]
    public void SandboxMcpKeyDisabled_DoesNotEnforce()
    {
        using var harness = CreateHarness("Production", new Dictionary<string, string?>
        {
            ["MCP_API_KEY"] = "disabled"
        });
        Assert.False(harness.Service.IsEnforced);
    }

    [Fact]
    public void ExplicitBillingEnforceFalse_DoesNotEnforce()
    {
        using var harness = CreateHarness("Production", new Dictionary<string, string?>
        {
            ["FLOWOS_BILLING_ENFORCE"] = "false"
        });
        Assert.False(harness.Service.IsEnforced);
    }

    [Fact]
    public async Task TrialTenant_WhenEnforced_IsDeniedWithPlanRequired()
    {
        using var harness = CreateHarness("Production", new Dictionary<string, string?>
        {
            ["FLOWOS_BILLING_ENFORCE"] = "true"
        });
        var tenant = new Tenant("Trial Billing Org");
        harness.Db.Tenants.Add(tenant);
        await harness.Db.SaveChangesAsync();

        var decision = await harness.Service.EnsureRuntimeAllowedAsync(tenant.TenantId);

        Assert.False(decision.Allowed);
        Assert.Equal(TenantEntitlementPolicy.PlanRequiredCode, decision.Code);
        Assert.Equal(TenantEntitlementPolicy.PlanRequiredMessage, decision.Message);
    }

    [Fact]
    public async Task ManagedActiveTenant_WhenEnforced_IsAllowed()
    {
        using var harness = CreateHarness("Production", new Dictionary<string, string?>
        {
            ["FLOWOS_BILLING_ENFORCE"] = "true"
        });
        var tenant = new Tenant("Paid Billing Org");
        tenant.AssignPlan(TenantPlan.Managed, TenantBillingStatus.Active);
        harness.Db.Tenants.Add(tenant);
        await harness.Db.SaveChangesAsync();

        var decision = await harness.Service.EnsureRuntimeAllowedAsync(tenant.TenantId);

        Assert.True(decision.Allowed);
        Assert.Null(decision.Code);
    }

    private static Harness CreateHarness(string environment, Dictionary<string, string?>? extra = null)
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new FlowOSDbContext(options);

        var values = extra ?? new Dictionary<string, string?>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(values!).Build();

        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);

        return new Harness(db, new TenantEntitlementService(db, env.Object, config));
    }

    private sealed class Harness : IDisposable
    {
        public Harness(FlowOSDbContext db, TenantEntitlementService service)
        {
            Db = db;
            Service = service;
        }

        public FlowOSDbContext Db { get; }
        public TenantEntitlementService Service { get; }

        public void Dispose() => Db.Dispose();
    }
}
