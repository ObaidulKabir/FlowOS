using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.DTOs.Orchestration;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities.Orchestration;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.UnitTests.Workflows;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.UnitTests.Integration;

/// <summary>
/// ADR-006 Phase 4A: HTTP surface for AgentWorkflowClass.
/// Follows <see cref="WorkflowClassesControllerTests"/> — in-memory database, tenant and
/// role supplied by request headers.
/// </summary>
public class AgentWorkflowClassesControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private const string Route = "/api/agent-workflow-classes";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public AgentWorkflowClassesControllerTests(CustomWebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_AgentWorkflowClassesController_" + Guid.NewGuid();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList())
                {
                    services.Remove(descriptor);
                }

                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList())
                {
                    services.Remove(descriptor);
                }

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

        _client = _factory.CreateClient();
        SetHeaders(_tenantId);
    }

    private void SetHeaders(Guid? tenantId, string role = "Admin")
    {
        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", role);

        if (tenantId.HasValue)
        {
            _client.DefaultRequestHeaders.Add("x-tenant-id", tenantId.Value.ToString());
        }
    }

    // ── Builders ──────────────────────────────────────────────────────────────

    private static AgentWorkflowBlueprint ValidBlueprint() => new()
    {
        Objective = "Assess quarterly portfolio risk",
        StartStepId = "gather",
        Steps = new List<AgentStepBlueprint>
        {
            new()
            {
                StepId = "gather",
                TeamRole = "DataAnalyst",
                NextSteps = new Dictionary<string, string> { ["Gathered"] = "assess" },
            },
            new()
            {
                StepId = "assess",
                TeamRole = "RiskAnalyst",
                NextSteps = new Dictionary<string, string> { ["Assessed"] = "END" },
            },
        },
    };

    /// <summary>A blueprint whose startStepId names no step — rejected as AWF-COMP-002.</summary>
    private static AgentWorkflowBlueprint DanglingStartBlueprint() =>
        ValidBlueprint() with { StartStepId = "nowhere" };

    private static object CreateRequest(
        string name = "QuarterlyRiskPlan",
        string version = "1.0.0",
        AgentWorkflowBlueprint? definition = null) =>
        new
        {
            name,
            version,
            definition = definition ?? ValidBlueprint(),
        };

    private async Task<AgentWorkflowClass> SeedAsync(
        Guid tenantId,
        string name = "SeededPlan",
        bool publish = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();

        var plan = new AgentWorkflowClass(tenantId, name, "1.0.0", ValidBlueprint(), DateTime.UtcNow);
        if (publish) plan.Publish(DateTime.UtcNow);

        db.AgentWorkflowClasses.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDraft_WithValidBlueprint_ReturnsCreatedDraft()
    {
        var response = await _client.PostAsJsonAsync(Route, CreateRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<AgentWorkflowClassResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal("QuarterlyRiskPlan", dto!.Name);
        Assert.Equal(WorkflowClassStatus.Draft, dto.Status);
        Assert.Equal("gather", dto.Definition.StartStepId);
    }

    [Fact]
    public async Task CreateDraft_WithoutTenantHeader_ReturnsUnauthorized()
    {
        SetHeaders(null);

        var response = await _client.PostAsJsonAsync(Route, CreateRequest("MissingTenant"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateDraft_WithDanglingStartStep_ReturnsBadRequestWithValidationErrors()
    {
        var response = await _client.PostAsJsonAsync(
            Route, CreateRequest("BrokenPlan", definition: DanglingStartBlueprint()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("AWF-COMP-002", body);
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ForOwnTenant_ReturnsPlanWithBlueprint()
    {
        var plan = await SeedAsync(_tenantId, "OwnPlan");

        var response = await _client.GetAsync($"{Route}/{plan.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AgentWorkflowClassResponseDto>();
        Assert.Equal("OwnPlan", dto!.Name);
        Assert.Equal(2, dto.Definition.Steps.Count);
    }

    [Fact]
    public async Task GetById_ForAnotherTenant_ReturnsNotFound()
    {
        // Queries are tenant-scoped, so another tenant's plan is indistinguishable from
        // one that does not exist. 404 rather than 403 keeps existence private.
        var plan = await SeedAsync(Guid.NewGuid(), "OtherTenantPlan");

        var response = await _client.GetAsync($"{Route}/{plan.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_UnknownId_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"{Route}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsOnlyTheCallersTenantPlans()
    {
        await SeedAsync(_tenantId, "MinePlanA");
        await SeedAsync(Guid.NewGuid(), "TheirPlan");

        var response = await _client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<AgentWorkflowClassResponseDto>>();
        Assert.NotNull(list);
        Assert.Contains(list!, p => p.Name == "MinePlanA");
        Assert.DoesNotContain(list!, p => p.Name == "TheirPlan");
    }

    [Theory]
    [InlineData("?take=0")]
    [InlineData("?take=500")]
    [InlineData("?skip=-1")]
    public async Task List_WithOutOfRangePaging_ReturnsBadRequest(string query)
    {
        var response = await _client.GetAsync($"{Route}{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLineage_ReturnsChainNewestFirst()
    {
        var first = await SeedAsync(_tenantId, "LineageV1", publish: true);

        var createResponse = await _client.PostAsJsonAsync(Route, new
        {
            name = "LineageV2",
            version = "2.0.0",
            definition = ValidBlueprint(),
            previousVersionId = first.Id,
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var second = await createResponse.Content.ReadFromJsonAsync<AgentWorkflowClassResponseDto>();

        var response = await _client.GetAsync($"{Route}/{second!.Id}/lineage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var chain = await response.Content.ReadFromJsonAsync<List<AgentWorkflowClassResponseDto>>();
        Assert.Equal(2, chain!.Count);
        Assert.Equal("LineageV2", chain[0].Name);
        Assert.Equal("LineageV1", chain[1].Name);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_Draft_ReturnsPublished()
    {
        var plan = await SeedAsync(_tenantId, "ToPublish");

        var response = await _client.PostAsync($"{Route}/{plan.Id}/publish", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AgentWorkflowClassResponseDto>();
        Assert.Equal(WorkflowClassStatus.Published, dto!.Status);
        Assert.NotNull(dto.PublishedAt);
    }

    [Fact]
    public async Task Publish_AlreadyPublished_ReturnsBadRequest()
    {
        var plan = await SeedAsync(_tenantId, "AlreadyPublished", publish: true);

        var response = await _client.PostAsync($"{Route}/{plan.Id}/publish", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDraft_WhenPublished_ReturnsBadRequest()
    {
        var plan = await SeedAsync(_tenantId, "ImmutablePlan", publish: true);

        var response = await _client.PutAsJsonAsync($"{Route}/{plan.Id}", CreateRequest("ImmutablePlan"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deprecate_Published_ReturnsDeprecatedWithReason()
    {
        var plan = await SeedAsync(_tenantId, "ToDeprecate", publish: true);

        var response = await _client.PostAsJsonAsync(
            $"{Route}/{plan.Id}/deprecate",
            new { reason = "Superseded by the 2027 model" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AgentWorkflowClassResponseDto>();
        Assert.Equal(WorkflowClassStatus.Deprecated, dto!.Status);
        Assert.Equal("Superseded by the 2027 model", dto.DeprecationReason);
    }

    [Fact]
    public async Task Abandon_Draft_ReturnsBadRequest()
    {
        // A Draft is deleted, never abandoned.
        var plan = await SeedAsync(_tenantId, "DraftToAbandon");

        var response = await _client.PostAsync($"{Route}/{plan.Id}/abandon", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Delete, and the governance guards ─────────────────────────────────────

    [Fact]
    public async Task Delete_Draft_ReturnsNoContent()
    {
        var plan = await SeedAsync(_tenantId, "DeletableDraft");

        var response = await _client.DeleteAsync($"{Route}/{plan.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{Route}/{plan.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_Published_ReturnsBadRequestWithGovDel001()
    {
        var plan = await SeedAsync(_tenantId, "PublishedPlan", publish: true);

        var response = await _client.DeleteAsync($"{Route}/{plan.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("GOV-DEL-001", await response.Content.ReadAsStringAsync());
    }

    // ── Validate-only ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_ValidBlueprint_ReturnsIsValidTrue()
    {
        var response = await _client.PostAsJsonAsync($"{Route}/validate", new { definition = ValidBlueprint() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"isvalid\":true", body.Replace(" ", "").ToLowerInvariant());
    }

    [Fact]
    public async Task Validate_BrokenBlueprint_ReturnsOkListingErrors()
    {
        // An invalid blueprint is a successful validation call reporting errors,
        // not a failed request — the author wants the whole list at once.
        var response = await _client.PostAsJsonAsync(
            $"{Route}/validate", new { definition = DanglingStartBlueprint() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("AWF-COMP-002", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Validate_WithoutDefinition_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync($"{Route}/validate", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
