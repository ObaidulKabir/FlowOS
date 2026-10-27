using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Core.Common.Models;
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
/// CR-10: the Idempotency-Key header makes start-workflow and publish-event safe to retry.
/// A repeat returns the first result, a repeat while the first is still running gets 409, and
/// invalid keys are rejected with 400.
/// </summary>
public class Idempotency_Key_E2E_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string EventType = "EVT-IDEMPOTENCY-TEST";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly string _workflowName = "IdempotencyFlow_" + Guid.NewGuid().ToString("N")[..8];

    public Idempotency_Key_E2E_Tests(WebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_E2E_Idempotency_" + Guid.NewGuid();
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

    private async Task SeedAsync()
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
            var evt = new FlowOS.Domain.Entities.EventDefinition(EventType, _tenantId, "Idempotency test", "Test", "System", FlowOS.Domain.Enums.EventCategory.System);
            evt.Publish();
            db.EventDefinitions.Add(evt);
        }

        var definition = new WorkflowDefinition(_tenantId, _workflowName);
        definition.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command) { NextSteps = new() { { EventType, "END" } } });
        definition.Publish();
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> PostAsync(string path, object body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (idempotencyKey != null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return _client.SendAsync(request);
    }

    private StartWorkflowCommand StartCommand(string? bodyKey = null) =>
        new(_tenantId, null, _workflowName, 1, Guid.Empty, "Start", Guid.NewGuid(), bodyKey);

    private static async Task<Guid> InstanceIdOf(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("workflowInstanceId").GetGuid();
    }

    private async Task<int> InstanceCountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var definitionIds = await db.WorkflowDefinitions.Where(d => d.Name == _workflowName).Select(d => d.Id).ToListAsync();
        return await db.WorkflowInstances.CountAsync(i => definitionIds.Contains(i.WorkflowDefinitionId));
    }

    [Fact]
    public async Task Repeated_start_with_the_same_key_returns_the_same_instance()
    {
        await SeedAsync();
        var key = "start-" + Guid.NewGuid();

        var first = await PostAsync("/api/workflows/start", StartCommand(), key);
        var second = await PostAsync("/api/workflows/start", StartCommand(), key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(await InstanceIdOf(first), await InstanceIdOf(second));
        Assert.Equal(1, await InstanceCountAsync());
    }

    [Fact]
    public async Task Different_keys_start_different_instances()
    {
        await SeedAsync();

        var first = await PostAsync("/api/workflows/start", StartCommand(), "start-" + Guid.NewGuid());
        var second = await PostAsync("/api/workflows/start", StartCommand(), "start-" + Guid.NewGuid());

        Assert.NotEqual(await InstanceIdOf(first), await InstanceIdOf(second));
        Assert.Equal(2, await InstanceCountAsync());
    }

    [Fact]
    public async Task Repeated_publish_with_the_same_key_applies_the_event_once()
    {
        await SeedAsync();
        var instanceId = await InstanceIdOf(await PostAsync("/api/workflows/start", StartCommand(), null));
        var key = "publish-" + Guid.NewGuid();
        var publish = new PublishEventCommand(_tenantId, instanceId, EventType);

        var first = await PostAsync("/api/events/publish", publish, key);
        // Without idempotency this would fail: the instance is already completed.
        var second = await PostAsync("/api/events/publish", publish, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var instance = await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == instanceId);
        Assert.Equal(WorkflowInstanceStatus.Completed, instance.Status);
        Assert.Equal(1, await db.Events.CountAsync(e => e.EventType == EventType && e.CorrelationId == instanceId));
    }

    [Fact]
    public async Task Repeat_while_the_first_request_is_still_running_is_409()
    {
        await SeedAsync();
        var instanceId = await InstanceIdOf(await PostAsync("/api/workflows/start", StartCommand(), null));
        var key = "inflight-" + Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            // A pending record is what the first request leaves while it is running.
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            db.IdempotencyRecords.Add(new IdempotencyRecord(_tenantId, "publish_event", key));
            await db.SaveChangesAsync();
        }

        var response = await PostAsync("/api/events/publish", new PublishEventCommand(_tenantId, instanceId, EventType), key);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("IDEMPOTENCY-IN-PROGRESS", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Header_and_body_keys_that_differ_are_rejected()
    {
        await SeedAsync();

        var response = await PostAsync("/api/workflows/start", StartCommand(bodyKey: "body-key"), "header-key");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await InstanceCountAsync());
    }

    [Fact]
    public async Task Key_longer_than_200_characters_is_rejected()
    {
        await SeedAsync();

        var response = await PostAsync("/api/workflows/start", StartCommand(), new string('k', 201));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await InstanceCountAsync());
    }
}
