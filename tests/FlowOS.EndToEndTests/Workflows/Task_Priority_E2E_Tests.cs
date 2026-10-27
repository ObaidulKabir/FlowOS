using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
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
/// CR-12: the task inbox is ordered by priority (instance override, else step), then SLA due time, then
/// age; it can be filtered and paged, and the priority of a running instance can be changed.
/// </summary>
public class Task_Priority_E2E_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Submit = "EVT-PRIO-SUBMIT";
    private const string Timeout = "EVT-PRIO-TIMEOUT";
    private const string Done = "EVT-PRIO-DONE";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly string _workflowName = "PriorityFlow_" + Guid.NewGuid().ToString("N")[..8];

    public Task_Priority_E2E_Tests(WebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_E2E_Priority_" + Guid.NewGuid();
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

        var admin = new Role(_tenantId, "Admin");
        admin.AddPermission("workflow.start");
        admin.AddPermission("event.publish");
        db.Roles.Add(admin);

        foreach (var id in new[] { Submit, Timeout, Done })
        {
            var evt = new FlowOS.Domain.Entities.EventDefinition(id, _tenantId, id, "Test", "System", FlowOS.Domain.Enums.EventCategory.System);
            evt.Publish();
            db.EventDefinitions.Add(evt);
        }

        var definition = new WorkflowDefinition(_tenantId, _workflowName);
        definition.AddStep(new WorkflowStepDefinition("Intake", WorkflowStepType.Command) { NextSteps = new() { { Submit, "Review" } } });
        definition.AddStep(new WorkflowStepDefinition("Review", WorkflowStepType.HumanTask)
        {
            Priority = 20,
            Sla = new StepSlaDefinition { Duration = "2h", TimeoutEvent = Timeout },
            NextSteps = new() { { Done, "END" }, { Timeout, "END" } }
        });
        definition.Publish();
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
    }

    /// <summary>Starts an instance and moves it into the Review human task.</summary>
    private async Task<Guid> StartReviewAsync(int? priority)
    {
        var started = await _client.PostAsJsonAsync("/api/workflows/start",
            new StartWorkflowCommand(_tenantId, WorkflowName: _workflowName, Version: 1, InitialStepId: "Intake", Priority: priority));
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var json = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("workflowInstanceId").GetGuid();

        var submitted = await _client.PostAsJsonAsync("/api/events/publish", new PublishEventCommand(_tenantId, id, Submit));
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        return id;
    }

    private async Task<(List<JsonElement> Tasks, int Total)> InboxAsync(string query = "")
    {
        var response = await _client.GetAsync("/api/tasks" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var total = int.Parse(response.Headers.GetValues("X-Total-Count").Single());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList(), total);
    }

    private static List<Guid> Ids(IEnumerable<JsonElement> tasks) => tasks.Select(t => t.GetProperty("taskId").GetGuid()).ToList();

    [Fact]
    public async Task Inbox_is_ordered_by_priority_then_due_time_then_age()
    {
        await SeedAsync();
        var stepDefault = await StartReviewAsync(null); // step priority 20
        var urgent = await StartReviewAsync(90);
        var medium = await StartReviewAsync(50);

        var (tasks, total) = await InboxAsync();

        Assert.Equal(3, total);
        Assert.Equal(new[] { urgent, medium, stepDefault }, Ids(tasks));
        var first = tasks[0];
        Assert.Equal(90, first.GetProperty("priority").GetInt32());
        Assert.Equal(20, tasks[2].GetProperty("priority").GetInt32());
        var due = first.GetProperty("dueAtUtc").GetDateTime();
        Assert.InRange(due, DateTime.UtcNow.AddMinutes(110), DateTime.UtcNow.AddMinutes(130)); // the 2h SLA
    }

    [Fact]
    public async Task Inbox_can_be_filtered_and_paged()
    {
        await SeedAsync();
        var low = await StartReviewAsync(10);
        var high = await StartReviewAsync(80);
        var mid = await StartReviewAsync(60);

        var (page, total) = await InboxAsync("?skip=1&take=1");
        Assert.Equal(3, total);
        Assert.Equal(new[] { mid }, Ids(page));

        var (important, importantTotal) = await InboxAsync("?minPriority=60");
        Assert.Equal(2, importantTotal);
        Assert.Equal(new[] { high, mid }, Ids(important));

        var (dueSoon, _) = await InboxAsync("?dueBefore=" + Uri.EscapeDataString(DateTime.UtcNow.AddHours(3).ToString("O")));
        Assert.Equal(3, dueSoon.Count);
        var (dueNow, _) = await InboxAsync("?dueBefore=" + Uri.EscapeDataString(DateTime.UtcNow.ToString("O")));
        Assert.Empty(dueNow);

        Assert.Contains(low, Ids((await InboxAsync()).Tasks));
    }

    [Fact]
    public async Task Priority_of_a_running_instance_can_be_changed_and_cleared()
    {
        await SeedAsync();
        var first = await StartReviewAsync(30);
        var second = await StartReviewAsync(40);

        var raised = await _client.PutAsJsonAsync($"/api/workflows/{first}/priority", new { priority = 100 });
        Assert.Equal(HttpStatusCode.OK, raised.StatusCode);
        Assert.Equal(new[] { first, second }, Ids((await InboxAsync()).Tasks));

        var cleared = await _client.PutAsJsonAsync($"/api/workflows/{first}/priority", new { priority = (int?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var tasks = (await InboxAsync()).Tasks;
        Assert.Equal(new[] { second, first }, Ids(tasks)); // 40 beats the step's 20
        Assert.Equal(20, tasks[1].GetProperty("priority").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        Assert.True(await db.Events.AnyAsync(e => e.EventType == "PriorityChanged" && e.CorrelationId == first));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Priority_outside_0_to_100_is_rejected(int priority)
    {
        await SeedAsync();
        var id = await StartReviewAsync(null);

        var update = await _client.PutAsJsonAsync($"/api/workflows/{id}/priority", new { priority });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);

        var start = await _client.PostAsJsonAsync("/api/workflows/start",
            new StartWorkflowCommand(_tenantId, WorkflowName: _workflowName, Version: 1, InitialStepId: "Intake", Priority: priority));
        Assert.Equal(HttpStatusCode.BadRequest, start.StatusCode);
    }

    [Fact]
    public async Task Unknown_instance_and_bad_paging_are_reported()
    {
        var missing = await _client.PutAsJsonAsync($"/api/workflows/{Guid.NewGuid()}/priority", new { priority = 10 });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/tasks?take=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/tasks?skip=-1")).StatusCode);
    }
}
