using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using FlowOS.UnitTests.Workflows;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.UnitTests.Integration;

public class TenantEntitlementApiTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly CustomWebApplicationFactory<Program> _factory;

    public TenantEntitlementApiTests(CustomWebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Development_DoesNotReturn402_ForTrialTenantStart()
    {
        var (client, tenantId, definitionId, workflowClassId) = CreateClient(enforceBilling: false, paid: false);

        var response = await client.PostAsJsonAsync("/api/workflows/start", new StartWorkflowCommand(
            tenantId,
            definitionId,
            null,
            1,
            workflowClassId,
            "Start",
            Guid.NewGuid()));

        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task TrialTenant_StartWorkflow_Returns402PlanRequired_UntilAdminActivates()
    {
        var (client, tenantId, definitionId, workflowClassId) = CreateClient(enforceBilling: true, paid: false);

        var denied = await client.PostAsJsonAsync("/api/workflows/start", new StartWorkflowCommand(
            tenantId,
            definitionId,
            null,
            1,
            workflowClassId,
            "Start",
            Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.PaymentRequired, denied.StatusCode);
        var body = await denied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(TenantEntitlementPolicy.PlanRequiredCode, body.GetProperty("code").GetString());
        Assert.Contains("MCP is included", body.GetProperty("message").GetString());

        var activate = await client.PostAsJsonAsync($"/api/admin/tenants/{tenantId}/plan", new
        {
            plan = "Managed",
            billingStatus = "Active"
        });
        activate.EnsureSuccessStatusCode();
        var activated = await activate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(activated.GetProperty("canRunRuntime").GetBoolean());

        var allowed = await client.PostAsJsonAsync("/api/workflows/start", new StartWorkflowCommand(
            tenantId,
            definitionId,
            null,
            1,
            workflowClassId,
            "Start",
            Guid.NewGuid()));

        Assert.NotEqual(HttpStatusCode.PaymentRequired, allowed.StatusCode);
        allowed.EnsureSuccessStatusCode();
    }

    private (HttpClient Client, Guid TenantId, Guid DefinitionId, Guid WorkflowClassId) CreateClient(
        bool enforceBilling,
        bool paid)
    {
        var dbName = "FlowOS_EntitlementApi_" + Guid.NewGuid();
        var tenant = new Tenant("Entitlement " + Guid.NewGuid().ToString("N")[..8]);
        if (paid)
            tenant.AssignPlan(TenantPlan.Managed, TenantBillingStatus.Active);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FLOWOS_BILLING_ENFORCE"] = enforceBilling ? "true" : "false"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var descriptor in contextDescriptors)
                    services.Remove(descriptor);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var descriptor in optionsDescriptors)
                    services.Remove(descriptor);

                services.AddScoped<FlowOSDbContext>(_ =>
                {
                    var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                        .UseInMemoryDatabase(dbName)
                        .EnableSensitiveDataLogging()
                        .Options;
                    return new TestFlowOSDbContext(options);
                });
            });
        });

        Guid definitionId;
        Guid workflowClassId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            db.Tenants.Add(tenant);

            var adminRole = new Role(tenant.TenantId, "Admin");
            adminRole.AddPermission("workflow.start");
            db.Roles.Add(adminRole);

            var workflowClass = new WorkflowClass(
                tenant.TenantId,
                "EntitlementFlow",
                "1.0.0",
                new WorkflowClassBlueprint
                {
                    StateMachine = new StateMachineBlueprint
                    {
                        InitialState = "Start",
                        States = new List<string> { "Start" }
                    },
                    Workflow = new WorkflowBlueprint
                    {
                        StartStepId = "Start",
                        Steps = new List<StepBlueprint>
                        {
                            new()
                            {
                                StepId = "Start",
                                StepType = "Command",
                                NextSteps = new Dictionary<string, string> { { "Default", "END" } }
                            }
                        }
                    }
                });
            db.WorkflowClasses.Add(workflowClass);

            var definition = new WorkflowDefinition(tenant.TenantId, "EntitlementFlow", 1, "Start");
            var step = new WorkflowStepDefinition("Start", WorkflowStepType.Command);
            step.NextSteps.Add("Default", "END");
            definition.AddStep(step);
            definition.Publish();
            db.WorkflowDefinitions.Add(definition);
            db.SaveChanges();
            definitionId = definition.Id;
            workflowClassId = workflowClass.Id;
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-tenant-id", tenant.TenantId.ToString());
        client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
        return (client, tenant.TenantId, definitionId, workflowClassId);
    }
}
