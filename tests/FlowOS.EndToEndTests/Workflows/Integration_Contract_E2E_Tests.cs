using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Domain.ValueObjects;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Workflows;

/// <summary>
/// CR-10: an integrating system (Learning OS) can find instances by its own reference, learns the
/// pinned definition and version from the start response, and sees causation and definition
/// version on every published event.
/// </summary>
public class Integration_Contract_E2E_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string EventType = "EVT-CONTRACT-TEST";
    private const string SourceSystem = "learning-os";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly string _workflowName = "ContractFlow_" + Guid.NewGuid().ToString("N")[..8];

    public Integration_Contract_E2E_Tests(WebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_E2E_Contract_" + Guid.NewGuid();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList()) services.Remove(d);
                foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList()) services.Remove(d);

                services.AddScoped<FlowOSDbContext>(provider =>
                {
                    var interceptor = provider.GetRequiredService<FlowOS.Notifications.Infrastructure.Persistence.EventPublishingInterceptor>();
                    var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                        .UseInMemoryDatabase(dbName)
                        .AddInterceptors(interceptor)
                        .Options;
                    return new TestFlowOSDbContext(options);
                });
            });
        });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
    }

    private async Task<Guid> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();

        if (!await db.Roles.AnyAsync(r => r.Name == "Admin" && r.TenantId == _tenantId))
        {
            var admin = new Role(_tenantId, "Admin");
            admin.AddPermission("workflow.start");
            admin.AddPermission("event.publish");
            db.Roles.Add(admin);
        }

        if (!await db.EventDefinitions.AnyAsync(e => e.EventId == EventType))
        {
            var evt = new FlowOS.Domain.Entities.EventDefinition(EventType, _tenantId, "Contract test", "Test", "System", FlowOS.Domain.Enums.EventCategory.System);
            evt.Publish();
            db.EventDefinitions.Add(evt);
        }

        var definition = new WorkflowDefinition(_tenantId, _workflowName);
        definition.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command) { NextSteps = new() { { EventType, "END" } } });
        definition.Publish();
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
        return definition.Id;
    }

    private StartWorkflowCommand Start(string? externalReference) => new(
        _tenantId,
        WorkflowName: _workflowName,
        Version: 1,
        InitialStepId: "Start",
        BusinessReference: externalReference == null
            ? null
            : new WorkflowBusinessReference { SourceSystem = SourceSystem, ExternalEntityId = externalReference });

    [Fact]
    public async Task Start_response_carries_the_pinned_definition_and_the_callers_reference()
    {
        var definitionId = await SeedAsync();

        var response = await _client.PostAsJsonAsync("/api/workflows/start", Start("recovery-case-123"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.NotEqual(Guid.Empty, root.GetProperty("workflowInstanceId").GetGuid());
        Assert.Equal(definitionId, root.GetProperty("workflowDefinitionId").GetGuid());
        Assert.Equal(1, root.GetProperty("workflowVersion").GetInt32());
        Assert.Equal(SourceSystem, root.GetProperty("externalSourceSystem").GetString());
        Assert.Equal("recovery-case-123", root.GetProperty("externalReference").GetString());
    }

    [Fact]
    public async Task Instances_can_be_found_by_the_callers_reference()
    {
        await SeedAsync();
        await _client.PostAsJsonAsync("/api/workflows/start", Start("recovery-case-A"));
        var started = await _client.PostAsJsonAsync("/api/workflows/start", Start("recovery-case-B"));
        await _client.PostAsJsonAsync("/api/workflows/start", Start(null));
        using var startedJson = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var expectedId = startedJson.RootElement.GetProperty("workflowInstanceId").GetGuid();

        var list = await _client.GetAsync($"/api/workflows?sourceSystem={SourceSystem}&externalReference=recovery-case-B");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var item = Assert.Single(listJson.RootElement.EnumerateArray());
        Assert.Equal(expectedId, item.GetProperty("id").GetGuid());
        Assert.Equal("recovery-case-B", item.GetProperty("externalReference").GetString());
    }

    [Fact]
    public async Task Published_events_carry_causation_and_the_pinned_definition()
    {
        var definitionId = await SeedAsync();
        var started = await _client.PostAsJsonAsync("/api/workflows/start", Start("recovery-case-C"));
        using var startedJson = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var instanceId = startedJson.RootElement.GetProperty("workflowInstanceId").GetGuid();
        var causationId = Guid.NewGuid();

        var publish = await _client.PostAsJsonAsync(
            "/api/events/publish",
            new PublishEventCommand(_tenantId, instanceId, EventType, CausationId: causationId));

        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var stored = await db.Events.AsNoTracking().SingleAsync(e => e.EventType == EventType && e.CorrelationId == instanceId);
        Assert.Equal(causationId.ToString(), stored.Metadata["CausationId"]);
        Assert.Equal(definitionId.ToString(), stored.Metadata["WorkflowDefinitionId"]);
        Assert.Equal("1", stored.Metadata["WorkflowVersion"]);
        Assert.Equal("recovery-case-C", stored.Metadata["ExternalEntityId"]);
    }

    [Fact]
    public async Task External_reference_over_200_characters_is_rejected()
    {
        await SeedAsync();

        var response = await _client.PostAsJsonAsync("/api/workflows/start", Start(new string('r', 201)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
