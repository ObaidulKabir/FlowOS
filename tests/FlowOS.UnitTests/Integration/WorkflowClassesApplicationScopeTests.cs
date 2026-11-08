using System.Net;
using System.Net.Http.Json;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.UnitTests.Workflows;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOS.UnitTests.Integration;

/// <summary>
/// Applications (4/4): an application-restricted FlowOS API key (TenantApiKey.IsApplicationRestricted)
/// is confined to its own application on the WorkflowClassesController surface - see docs/14-api-reference.md
/// section 8 and ApplicationScopeRules. Mirrors WorkflowClassesControllerTests' fixture style, but
/// authenticates with a real seeded TenantApiKey (X-API-Key) instead of the X-Mock-Role/x-tenant-id
/// headers, since those always resolve to an unrestricted caller.
/// </summary>
public class WorkflowClassesApplicationScopeTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public WorkflowClassesApplicationScopeTests(CustomWebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_WorkflowClassesAppScope_" + Guid.NewGuid();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList())
                    services.Remove(descriptor);
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList())
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

        _client = _factory.CreateClient();
    }

    private static WorkflowClassBlueprint ValidBlueprint() => new()
    {
        Events = new List<EventBlueprint> { new() { EventId = "EVT-DONE", Name = "Done" } },
        StateMachine = new StateMachineBlueprint { InitialState = "S1", States = new List<string> { "S1" } },
        Workflow = new WorkflowBlueprint
        {
            StartStepId = "S1",
            Steps = new List<StepBlueprint>
            {
                new() { StepId = "S1", StepType = "Command", NextSteps = new Dictionary<string, string> { { "EVT-DONE", "END" } } }
            }
        }
    };

    private static WorkflowClass NewWorkflowClass(Guid tenantId, string name, Guid? applicationId = null)
    {
        var wc = new WorkflowClass(tenantId, name, "1.0.0", ValidBlueprint());
        if (applicationId.HasValue)
            wc.AssignToApplication(applicationId.Value);
        return wc;
    }

    /// <summary>
    /// Seeds appA/appB and a workflow class in each (plus one unassigned), seeds a TenantApiKey restricted
    /// to appA, and points the HTTP client at it. Returns the ids the tests assert against.
    /// </summary>
    private async Task<(Guid AppAId, Guid AppBId, Guid InAppA, Guid InAppB, Guid Unassigned)> SeedAsync()
    {
        var appA = new TenantApplication(_tenantId, "App A", null, DateTime.UtcNow);
        var appB = new TenantApplication(_tenantId, "App B", null, DateTime.UtcNow);
        var inAppA = NewWorkflowClass(_tenantId, "InAppA", appA.Id);
        var inAppB = NewWorkflowClass(_tenantId, "InAppB", appB.Id);
        var unassigned = NewWorkflowClass(_tenantId, "Unassigned");

        var rawKey = TenantApiKey.GenerateRawKey();
        var apiKey = new TenantApiKey(_tenantId, "Scoped key", rawKey);
        apiKey.IssueForApplication(appA);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            db.TenantApplications.AddRange(appA, appB);
            db.WorkflowClasses.AddRange(inAppA, inAppB, unassigned);
            db.TenantApiKeys.Add(apiKey);
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Remove("X-API-Key");
        _client.DefaultRequestHeaders.Add("X-API-Key", rawKey);

        return (appA.Id, appB.Id, inAppA.Id, inAppB.Id, unassigned.Id);
    }

    [Fact]
    public async Task GetById_OwnApplicationsWorkflow_Succeeds()
    {
        var (_, _, inAppA, _, _) = await SeedAsync();

        var response = await _client.GetAsync($"/api/workflow-classes/{inAppA}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("InAppB")]
    [InlineData("Unassigned")]
    public async Task GetById_AnotherApplicationsOrUnassignedWorkflow_ReturnsNotFound(string which)
    {
        var (_, _, _, inAppB, unassigned) = await SeedAsync();
        var id = which == "InAppB" ? inAppB : unassigned;

        var response = await _client.GetAsync($"/api/workflow-classes/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_IgnoresCallersApplicationIdAndUnassignedQuery_ReturnsOnlyTheKeysOwnApplication()
    {
        var (_, appBId, _, _, _) = await SeedAsync();

        var response = await _client.GetAsync($"/api/workflow-classes?applicationId={appBId}&unassigned=true");

        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<WorkflowClassResponseDto>>();

        Assert.NotNull(items);
        Assert.Contains(items!, item => item.Name == "InAppA");
        Assert.DoesNotContain(items!, item => item.Name == "InAppB");
        Assert.DoesNotContain(items!, item => item.Name == "Unassigned");
    }

    [Fact]
    public async Task CreateDraft_PlacesTheNewWorkflowInTheKeysOwnApplication_RegardlessOfBody()
    {
        var (appAId, _, _, _, _) = await SeedAsync();

        var response = await _client.PostAsJsonAsync("/api/workflow-classes", new CreateWorkflowClassRequest
        {
            Name = "NewDraft",
            Version = "1.0.0",
            Definition = ValidBlueprint()
        });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();
        Assert.Equal(appAId, created!.ApplicationId);
    }

    [Fact]
    public async Task CreateDraft_RequestingADifferentApplication_ReturnsForbidden()
    {
        var (_, appBId, _, _, _) = await SeedAsync();

        var response = await _client.PostAsJsonAsync("/api/workflow-classes", new CreateWorkflowClassRequest
        {
            Name = "NewDraft",
            Version = "1.0.0",
            Definition = ValidBlueprint(),
            ApplicationId = appBId
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("APP-SCOPE-001", body);
    }

    [Fact]
    public async Task AssignApplication_IsRefusedOutrightForARestrictedKey()
    {
        var (appAId, _, inAppA, _, _) = await SeedAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/workflow-classes/{inAppA}/application",
            new AssignWorkflowClassApplicationRequest { ApplicationId = appAId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("APP-SCOPE-001", body);
    }

    [Fact]
    public async Task Delete_AnotherApplicationsWorkflow_ReturnsNotFound_AndNeverReachesTheHandler()
    {
        var (_, _, _, inAppB, _) = await SeedAsync();

        var response = await _client.DeleteAsync($"/api/workflow-classes/{inAppB}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        Assert.NotNull(await db.WorkflowClasses.FindAsync(inAppB));
    }

    [Fact]
    public async Task Delete_OwnApplicationsWorkflow_Succeeds()
    {
        var (_, _, inAppA, _, _) = await SeedAsync();

        var response = await _client.DeleteAsync($"/api/workflow-classes/{inAppA}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UnrestrictedKey_IsUnaffectedByAnyOfTheAboveScoping()
    {
        var appA = new TenantApplication(_tenantId, "App A", null, DateTime.UtcNow);
        var appB = new TenantApplication(_tenantId, "App B", null, DateTime.UtcNow);
        var inAppB = NewWorkflowClass(_tenantId, "InAppB", appB.Id);
        var rawKey = TenantApiKey.GenerateRawKey();
        // Never IssueForApplication-ed, so IsApplicationRestricted stays false - tenant-wide access.
        var unrestrictedKey = new TenantApiKey(_tenantId, "Unrestricted key", rawKey);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            db.TenantApplications.AddRange(appA, appB);
            db.WorkflowClasses.Add(inAppB);
            db.TenantApiKeys.Add(unrestrictedKey);
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Remove("X-API-Key");
        _client.DefaultRequestHeaders.Add("X-API-Key", rawKey);

        var response = await _client.GetAsync($"/api/workflow-classes/{inAppB.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
