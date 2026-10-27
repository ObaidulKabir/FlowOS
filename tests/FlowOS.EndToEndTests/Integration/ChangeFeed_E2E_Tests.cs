using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Api.Http;
using FlowOS.Application.Common;
using FlowOS.Application.Commands;
using FlowOS.Domain.ValueObjects;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Integration;

/// <summary>
/// CR-10c: Learning OS pulls FlowOS events with a cursor, in storage order, scoped to its tenant,
/// signed with HMAC, in the shape published in docs/schemas/change-feed.v1.schema.json.
/// </summary>
public class ChangeFeed_E2E_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string EventType = "EVT-FEED-TEST";
    private const string SigningKey = "change-feed-test-signing-key";
    private readonly WebApplicationFactory<Program> _baseFactory;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _dbName = "FlowOS_E2E_ChangeFeed_" + Guid.NewGuid();
    // Shared so a second host (the settle-window test) reads the same in-memory store.
    private readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot _dbRoot = new();
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _otherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly string _workflowName = "FeedFlow_" + Guid.NewGuid().ToString("N")[..8];

    public ChangeFeed_E2E_Tests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
        _factory = CreateFactory(settleSeconds: 0);
        _client = CreateClient(_factory, _tenantId);
    }

    private WebApplicationFactory<Program> CreateFactory(int settleSeconds) => _baseFactory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FlowOS:ChangeFeed:SettleSeconds"] = settleSeconds.ToString(),
            ["FlowOS:ChangeFeed:SigningKey"] = SigningKey
        }));
        builder.ConfigureTestServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList()) services.Remove(d);
            foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList()) services.Remove(d);

            services.AddScoped<FlowOSDbContext>(provider =>
            {
                var interceptor = provider.GetRequiredService<FlowOS.Notifications.Infrastructure.Persistence.EventPublishingInterceptor>();
                var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                    .UseInMemoryDatabase(_dbName, _dbRoot)
                    .AddInterceptors(interceptor)
                    .Options;
                return new TestFlowOSDbContext(options);
            });
        });
    });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, Guid tenantId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-tenant-id", tenantId.ToString());
        client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
        return client;
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();

        var admin = new Role(_tenantId, "Admin");
        admin.AddPermission("workflow.start");
        admin.AddPermission("event.publish");
        db.Roles.Add(admin);

        var evt = new FlowOS.Domain.Entities.EventDefinition(EventType, _tenantId, "Feed test", "Test", "System", FlowOS.Domain.Enums.EventCategory.System);
        evt.Publish();
        db.EventDefinitions.Add(evt);

        var definition = new WorkflowDefinition(_tenantId, _workflowName);
        definition.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command) { NextSteps = new() { { EventType, "END" } } });
        definition.Publish();
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
    }

    /// <summary>Starts an instance with a reference and publishes one event with causation and payload.</summary>
    private async Task<(Guid InstanceId, Guid CausationId)> RunOneCaseAsync(string reference)
    {
        var started = await _client.PostAsJsonAsync("/api/workflows/start", new StartWorkflowCommand(
            _tenantId,
            WorkflowName: _workflowName,
            Version: 1,
            InitialStepId: "Start",
            BusinessReference: new WorkflowBusinessReference { SourceSystem = "learning-os", ExternalEntityId = reference }));
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var json = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var instanceId = json.RootElement.GetProperty("workflowInstanceId").GetGuid();

        var causationId = Guid.NewGuid();
        var published = await _client.PostAsJsonAsync("/api/events/publish",
            new PublishEventCommand(_tenantId, instanceId, EventType, Payload: new { score = 7, reference }, CausationId: causationId));
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        return (instanceId, causationId);
    }

    private static async Task<(JsonDocument Json, HttpResponseMessage Response, byte[] Body)> GetPageAsync(HttpClient client, string? after = null, int? limit = null)
    {
        var url = "/api/changes";
        var query = new List<string>();
        if (after != null) query.Add("after=" + Uri.EscapeDataString(after));
        if (limit != null) query.Add("limit=" + limit);
        if (query.Count > 0) url += "?" + string.Join("&", query);

        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url} returned {(int)response.StatusCode}: {Encoding.UTF8.GetString(body)}");
        return (JsonDocument.Parse(body), response, body);
    }

    [Fact]
    public async Task Feed_returns_events_in_order_with_workflow_and_caller_references()
    {
        await SeedAsync();
        var (instanceId, causationId) = await RunOneCaseAsync("case-feed-1");

        var (json, _, _) = await GetPageAsync(_client);
        var root = json.RootElement;

        Assert.Equal("flowos.change-feed/v1", root.GetProperty("schema").GetString());
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.True(items.Count >= 2, $"expected the start and the published event, got {items.Count}");
        var sequences = items.Select(i => i.GetProperty("sequence").GetInt64()).ToList();
        Assert.Equal(sequences.OrderBy(s => s).ToList(), sequences);
        Assert.Equal(sequences.Count, sequences.Distinct().Count());

        var published = Assert.Single(items, i => i.GetProperty("eventType").GetString() == EventType);
        Assert.Equal(instanceId, published.GetProperty("workflowInstanceId").GetGuid());
        Assert.Equal(causationId, published.GetProperty("causationId").GetGuid());
        Assert.Equal(1, published.GetProperty("workflowVersion").GetInt32());
        Assert.Equal("learning-os", published.GetProperty("externalSourceSystem").GetString());
        Assert.Equal("case-feed-1", published.GetProperty("externalReference").GetString());
        Assert.Equal(7, published.GetProperty("payload").GetProperty("score").GetInt32());
        Assert.False(published.GetProperty("metadata").TryGetProperty("Payload", out _), "payload is not repeated in metadata");

        // The start event has no WorkflowInstanceId metadata; it is resolved through the correlation ID.
        var startEvent = items.First(i => i.GetProperty("sequence").GetInt64() < published.GetProperty("sequence").GetInt64()
                                          && i.GetProperty("correlationId").ValueKind == JsonValueKind.String
                                          && i.GetProperty("correlationId").GetGuid() == instanceId);
        Assert.Equal("case-feed-1", startEvent.GetProperty("externalReference").GetString());
        Assert.False(root.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Walking_the_feed_one_item_at_a_time_returns_every_event_once()
    {
        await SeedAsync();
        await RunOneCaseAsync("case-walk-1");
        await RunOneCaseAsync("case-walk-2");
        var (all, _, _) = await GetPageAsync(_client);
        var expected = all.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("eventId").GetGuid()).ToList();

        var seen = new List<Guid>();
        string? cursor = null;
        for (var guard = 0; guard < 50; guard++)
        {
            var (page, _, _) = await GetPageAsync(_client, cursor, limit: 1);
            var items = page.RootElement.GetProperty("items").EnumerateArray().ToList();
            seen.AddRange(items.Select(i => i.GetProperty("eventId").GetGuid()));
            var next = page.RootElement.GetProperty("nextCursor").GetString();
            if (!page.RootElement.GetProperty("hasMore").GetBoolean())
            {
                // The last cursor is stable: polling it again returns nothing new.
                var (again, _, _) = await GetPageAsync(_client, next, limit: 1);
                Assert.Empty(again.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(next, again.RootElement.GetProperty("nextCursor").GetString());
                break;
            }
            Assert.Single(items);
            cursor = next;
        }

        Assert.Equal(expected, seen);

        // Re-reading from an earlier cursor returns the same page again (safe retry after a crash).
        var (first, _, _) = await GetPageAsync(_client, null, limit: 1);
        var (firstAgain, _, _) = await GetPageAsync(_client, null, limit: 1);
        Assert.Equal(first.RootElement.GetProperty("items").GetRawText(), firstAgain.RootElement.GetProperty("items").GetRawText());
    }

    [Fact]
    public async Task Another_tenant_sees_none_of_these_events()
    {
        await SeedAsync();
        await RunOneCaseAsync("case-tenant-1");
        var (mine, _, _) = await GetPageAsync(_client);
        var myIds = mine.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("eventId").GetGuid()).ToHashSet();
        Assert.NotEmpty(myIds);

        var other = CreateClient(_factory, _otherTenantId);
        var (theirs, _, _) = await GetPageAsync(other);

        Assert.DoesNotContain(theirs.RootElement.GetProperty("items").EnumerateArray(), i => myIds.Contains(i.GetProperty("eventId").GetGuid()));
    }

    [Fact]
    public async Task Response_is_signed_with_the_configured_key()
    {
        await SeedAsync();
        await RunOneCaseAsync("case-signed-1");

        var (_, response, body) = await GetPageAsync(_client);

        Assert.True(response.Headers.TryGetValues(ChangeFeedSignature.HeaderName, out var values));
        var header = Assert.Single(values);
        var key = Encoding.UTF8.GetBytes(SigningKey);
        Assert.True(ChangeFeedSignature.Verify(key, header, body, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)));
        var tampered = (byte[])body.Clone();
        tampered[^2] ^= 1;
        Assert.False(ChangeFeedSignature.Verify(key, header, tampered, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task Response_matches_the_published_v1_schema()
    {
        await SeedAsync();
        await RunOneCaseAsync("case-schema-1");
        using var schema = JsonSchemaLite.LoadRepositorySchema("docs/schemas/change-feed.v1.schema.json");

        var (json, _, _) = await GetPageAsync(_client);

        Assert.NotEmpty(json.RootElement.GetProperty("items").EnumerateArray());
        var errors = JsonSchemaLite.Validate(schema.RootElement, json.RootElement);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        // The checker itself rejects a field the schema does not allow.
        using var extra = JsonDocument.Parse(json.RootElement.GetRawText().TrimEnd('}') + ",\"unexpected\":1}");
        Assert.Contains(JsonSchemaLite.Validate(schema.RootElement, extra.RootElement), e => e.Contains("unexpected"));
    }

    [Theory]
    [InlineData("after=not-a-cursor")]
    [InlineData("limit=0")]
    [InlineData("limit=501")]
    public async Task Invalid_cursor_or_limit_is_a_bad_request(string query)
    {
        var response = await _client.GetAsync("/api/changes?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Events_inside_the_settle_window_are_held_back_without_moving_the_cursor()
    {
        await SeedAsync();
        await RunOneCaseAsync("case-settle-1");
        var settling = CreateClient(CreateFactory(settleSeconds: 3600), _tenantId);

        var (json, _, _) = await GetPageAsync(settling);

        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(json.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Equal(ChangeFeedCursor.Encode(0), json.RootElement.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("/api/changes");

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
    }
}
