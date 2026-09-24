using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlowOS.Application.DTOs.Admin;
using FlowOS.Core.Security;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Interfaces;
using FlowOS.UnitTests.Workflows;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOS.UnitTests.Integration;

public class AuditTrailApiTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly CustomWebApplicationFactory<Program> _factory;

    public AuditTrailApiTests(CustomWebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MockAuthDisabled_Audit_RejectsMissingCredentials()
    {
        var tenantId = Guid.NewGuid();
        var factory = CreateFactory();
        var instanceId = await SeedRunningInstanceAsync(factory, tenantId, Guid.NewGuid());
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/workflows/{instanceId}/audit");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MockAuthDisabled_Audit_ReturnsInstanceAndBusinessEvents_ForDemoKey()
    {
        var tenantId = TenantIdentityRules.DemoTenantId;
        var businessCorrelation = Guid.NewGuid();
        var factory = CreateFactory();
        var instanceId = await SeedRunningInstanceAsync(factory, tenantId, businessCorrelation);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-tenant-id", tenantId.ToString());
        client.DefaultRequestHeaders.Add("X-API-Key", "flowos_prod_secret_key_32_chars_min");

        var response = await client.GetAsync($"/api/workflows/{instanceId}/audit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var audit = await response.Content.ReadFromJsonAsync<AdminWorkflowDetailDto>();
        Assert.NotNull(audit);
        Assert.Equal(instanceId, audit!.Id);
        Assert.Equal("AuditTrail", audit.DefinitionName);
        Assert.Equal("Draft", audit.CurrentState);
        Assert.Equal(businessCorrelation, audit.CorrelationId);
        Assert.Contains(audit.Timeline, item => item.EventType == "WorkflowStarted");
        Assert.Contains(audit.Timeline, item => item.EventType == "EVT-APPROVE");
        Assert.DoesNotContain(audit.Timeline, item => item.EventType == "EVT-FOREIGN");
    }

    [Fact]
    public async Task MockAuthDisabled_Audit_AcceptsJwtWhenApiKeyIsUnknown()
    {
        var tenantId = Guid.NewGuid();
        var factory = CreateFactory();
        var instanceId = await SeedRunningInstanceAsync(factory, tenantId, Guid.NewGuid());
        using var scope = factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateToken(Guid.NewGuid(), "audit@flowos.test", "Audit User", tenantId, "Audit Tenant", "Tenant");

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-tenant-id", tenantId.ToString());
        client.DefaultRequestHeaders.Add("X-API-Key", "stale-or-unknown-key");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/workflows/{instanceId}/audit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var audit = await response.Content.ReadFromJsonAsync<AdminWorkflowDetailDto>();
        Assert.NotNull(audit);
        Assert.Contains(audit!.Timeline, item => item.EventType == "WorkflowStarted");
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        var dbName = "FlowOS_AuditTrail_" + Guid.NewGuid();
        return _factory.WithWebHostBuilder(builder =>
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

    private static async Task<Guid> SeedRunningInstanceAsync(
        WebApplicationFactory<Program> factory,
        Guid tenantId,
        Guid businessCorrelation)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();

        var definition = new WorkflowDefinition(tenantId, "AuditTrail", 1, "Draft");
        var step = new WorkflowStepDefinition("Draft", WorkflowStepType.Command);
        definition.AddStep(step);
        definition.Publish();
        db.WorkflowDefinitions.Add(definition);

        var instance = new WorkflowInstance(
            tenantId,
            definition.Id,
            Guid.Empty,
            definition.Version,
            "Draft",
            businessCorrelation,
            "Draft");
        db.WorkflowInstances.Add(instance);

        var started = new StandardEvent(tenantId, "WorkflowStarted");
        started.SetCorrelationId(instance.Id);
        started.AddMetadata("StartStep", "Draft");
        db.Events.Add(started);

        var approved = new StandardEvent(tenantId, "EVT-APPROVE");
        approved.SetCorrelationId(businessCorrelation);
        approved.AddMetadata("ToState", "Approved");
        db.Events.Add(approved);

        var foreign = new StandardEvent(Guid.NewGuid(), "EVT-FOREIGN");
        foreign.SetCorrelationId(instance.Id);
        db.Events.Add(foreign);

        await db.SaveChangesAsync();
        return instance.Id;
    }
}
