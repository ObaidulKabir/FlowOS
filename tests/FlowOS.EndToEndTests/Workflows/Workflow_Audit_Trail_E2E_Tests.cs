using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowOS.Application.Commands;
using FlowOS.Core.Security;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Interfaces;
using FlowOS.Security.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOS.EndToEndTests.Workflows;

public class Workflow_Audit_Trail_E2E_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public Workflow_Audit_Trail_E2E_Tests(WebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_AuditTrail_E2E_" + Guid.NewGuid();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FlowOS:Identity:AllowMockAuth"] = "false",
                    ["UseInMemoryDatabase"] = "true",
                    ["ConnectionStrings:DefaultConnection"] = ""
                });
            });
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(d =>
                             d.ServiceType == typeof(FlowOSDbContext) ||
                             d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList())
                {
                    services.Remove(descriptor);
                }

                services.AddScoped<FlowOSDbContext>(_ =>
                {
                    var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                        .UseInMemoryDatabase(dbName)
                        .Options;
                    return new TestFlowOSDbContext(options);
                });
            });
        });
    }

    [Fact]
    public async Task Started_Workflow_Audit_Requires_Credentials_And_Returns_WorkflowStarted()
    {
        await EnsureAuditWorkflowAsync();
        var tenantId = TenantIdentityRules.DemoTenantId;

        var anonymous = _factory.CreateClient();
        var denied = await anonymous.GetAsync($"/api/workflows/{Guid.NewGuid()}/audit");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-tenant-id", tenantId.ToString());
        client.DefaultRequestHeaders.Add("X-API-Key", "flowos_prod_secret_key_32_chars_min");

        var start = await client.PostAsJsonAsync("/api/workflows/start", new StartWorkflowCommand(
            tenantId,
            WorkflowName: "AuditTrailProbe",
            CorrelationId: Guid.NewGuid()));
        var startBody = await start.Content.ReadAsStringAsync();
        Assert.True(start.StatusCode == HttpStatusCode.OK, startBody);

        using var startedJson = JsonDocument.Parse(startBody);
        var instanceId = startedJson.RootElement.GetProperty("workflowInstanceId").GetGuid();

        var audit = await client.GetAsync($"/api/workflows/{instanceId}/audit");
        var auditBody = await audit.Content.ReadAsStringAsync();
        Assert.True(audit.StatusCode == HttpStatusCode.OK, auditBody);

        using var auditJson = JsonDocument.Parse(auditBody);
        var root = auditJson.RootElement;
        Assert.Equal(instanceId, root.GetProperty("id").GetGuid());
        Assert.Equal("AuditTrailProbe", root.GetProperty("definitionName").GetString());
        Assert.Equal("Draft", root.GetProperty("currentState").GetString());
        Assert.Contains(
            root.GetProperty("timeline").EnumerateArray(),
            item => item.GetProperty("eventType").GetString() == "WorkflowStarted");

        var actions = await client.GetAsync($"/api/workflows/{instanceId}/actions");
        Assert.Equal(HttpStatusCode.OK, actions.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateToken(
            Guid.NewGuid(),
            "audit@flowos.test",
            "Audit User",
            tenantId,
            "Demo Client Tenant",
            "TenantAdmin");
        var jwtClient = _factory.CreateClient();
        jwtClient.DefaultRequestHeaders.Add("x-tenant-id", tenantId.ToString());
        jwtClient.DefaultRequestHeaders.Add("X-API-Key", "stale-or-unknown-key");
        jwtClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var jwtAudit = await jwtClient.GetAsync($"/api/workflows/{instanceId}/audit");
        Assert.Equal(HttpStatusCode.OK, jwtAudit.StatusCode);
    }

    private async Task EnsureAuditWorkflowAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var tenantId = TenantIdentityRules.DemoTenantId;

        var tenant = await db.Tenants.FirstOrDefaultAsync(item => item.TenantId == tenantId);
        if (tenant == null)
        {
            tenant = new Tenant("Demo Client Tenant");
            typeof(Tenant).GetProperty(nameof(Tenant.TenantId))!.SetValue(tenant, tenantId);
            db.Tenants.Add(tenant);
        }
        if (!tenant.CanRunRuntime)
            tenant.AssignPlan(TenantPlan.Managed, TenantBillingStatus.Active);

        var admin = await db.Roles.FirstOrDefaultAsync(role => role.TenantId == tenantId && role.Name == "Admin");
        if (admin == null)
        {
            admin = new Role(tenantId, "Admin");
            db.Roles.Add(admin);
        }
        admin.AddPermission("workflow.start");
        admin.AddPermission("workflow.read");

        if (!await db.WorkflowDefinitions.AnyAsync(item => item.TenantId == tenantId && item.Name == "AuditTrailProbe"))
        {
            var blueprint = new WorkflowClassBlueprint
            {
                StateMachine = new StateMachineBlueprint
                {
                    InitialState = "Draft",
                    States = new() { "Draft" }
                },
                Workflow = new WorkflowBlueprint
                {
                    StartStepId = "Draft",
                    Steps = new()
                    {
                        new StepBlueprint { StepId = "Draft", StepType = "Command" }
                    }
                }
            };
            var workflowClass = new WorkflowClass(tenantId, "AuditTrailProbe", "1.0.0", blueprint);
            new WorkflowClassManager().Publish(workflowClass);
            db.WorkflowClasses.Add(workflowClass);

            var definition = new WorkflowDefinition(tenantId, "AuditTrailProbe", 1, "Draft");
            definition.AddStep(new WorkflowStepDefinition("Draft", WorkflowStepType.Command));
            definition.Publish();
            db.WorkflowDefinitions.Add(definition);
        }

        await db.SaveChangesAsync();
    }
}
