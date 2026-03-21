using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Governance;

public class TenantDashboard_Simulation_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public TenantDashboard_Simulation_Tests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);
                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_Dashboard_" + Guid.NewGuid();
                services.AddScoped<FlowOSDbContext>(provider => 
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

    private void SetupTenantHeaders()
    {
        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Designer");
    }

    [Fact]
    public async Task Dashboard_Full_Lifecycle_Simulation()
    {
        SetupTenantHeaders();
        
        // ---------------------------------------------------------
        // 1. DRAFTS TAB: Create -> List -> Validate -> Delete
        // ---------------------------------------------------------
        
        // Create Draft
        var draftReq = new CreateWorkflowClassRequest
        {
            Name = "DraftProcess",
            Version = "0.1.0",
            Definition = new WorkflowClassBlueprint
            {
                // Minimal Valid
                Events = new() { new EventBlueprint { EventId = "EVT-START", Name = "Start" } },
                StateMachine = new StateMachineBlueprint { InitialState = "S1", States = new() { "S1" } },
                Workflow = new WorkflowBlueprint 
                { 
                    StartStepId = "S1",
                    Steps = new() { new StepBlueprint { StepId = "S1", StepType = "End" } } 
                }
            }
        };
        var createResp = await _client.PostAsJsonAsync("/api/workflow-classes", draftReq);
        createResp.EnsureSuccessStatusCode();
        var draft = await createResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();
        
        // List Drafts (Simulate Tab Filter)
        var listDraftsResp = await _client.GetAsync("/api/workflow-classes?status=Draft");
        listDraftsResp.EnsureSuccessStatusCode();
        var drafts = await listDraftsResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto[]>();
        Assert.Contains(drafts, d => d.Id == draft.Id);

        // Validate Explicitly
        var valResp = await _client.PostAsync($"/api/workflow-classes/{draft.Id}/validate", null);
        valResp.EnsureSuccessStatusCode(); // Returns ValidationResult
        
        // Delete Draft
        var delResp = await _client.DeleteAsync($"/api/workflow-classes/{draft.Id}");
        delResp.EnsureSuccessStatusCode();
        
        // Verify Deleted
        var getDelResp = await _client.GetAsync($"/api/workflow-classes/{draft.Id}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, getDelResp.StatusCode);


        // ---------------------------------------------------------
        // 2. PUBLISHED TAB: Create -> Publish -> List -> Deprecate
        // ---------------------------------------------------------

        // Re-create for publishing
        draftReq = draftReq with { Name = "PublishedProcess" };
        var pubCreateResp = await _client.PostAsJsonAsync("/api/workflow-classes", draftReq);
        var pubDraft = await pubCreateResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();
        
        // Publish
        var publishResp = await _client.PostAsync($"/api/workflow-classes/{pubDraft.Id}/publish", null);
        publishResp.EnsureSuccessStatusCode();

        // List Published (Simulate Tab Filter)
        var listPubResp = await _client.GetAsync("/api/workflow-classes?status=Published");
        var published = await listPubResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto[]>();
        Assert.Contains(published, d => d.Id == pubDraft.Id);

        // Deprecate
        var depResp = await _client.PostAsync($"/api/workflow-classes/{pubDraft.Id}/deprecate", null);
        depResp.EnsureSuccessStatusCode();
        
        // Verify Status
        var depGetResp = await _client.GetAsync($"/api/workflow-classes/{pubDraft.Id}");
        var depWc = await depGetResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();
        Assert.Equal(WorkflowClassStatus.Deprecated, depWc.Status);


        // ---------------------------------------------------------
        // 3. SHARED TAB: Create -> Publish -> Submit -> Withdraw
        // ---------------------------------------------------------
        
        draftReq = draftReq with { Name = "SharedProcess" };
        var shaCreateResp = await _client.PostAsJsonAsync("/api/workflow-classes", draftReq);
        var shaDraft = await shaCreateResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();
        
        await _client.PostAsync($"/api/workflow-classes/{shaDraft.Id}/publish", null);
        
        // Submit
        var subResp = await _client.PostAsync($"/api/workflow-classes/{shaDraft.Id}/submit", null);
        subResp.EnsureSuccessStatusCode();

        // List Shared
        var listShaResp = await _client.GetAsync("/api/workflow-classes?scope=Shared");
        var shared = await listShaResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto[]>();
        Assert.Contains(shared, d => d.Id == shaDraft.Id);

        // Withdraw
        var withResp = await _client.PostAsync($"/api/workflow-classes/{shaDraft.Id}/withdraw", null);
        withResp.EnsureSuccessStatusCode();
        
        // Verify Reverted to Published/Private
        var withGetResp = await _client.GetAsync($"/api/workflow-classes/{shaDraft.Id}");
        var withWc = await withGetResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();
        Assert.Equal(WorkflowClassStatus.Published, withWc.Status);
        Assert.Equal(WorkflowClassScope.Private, withWc.Scope);


        // ---------------------------------------------------------
        // 4. PUBLIC TAB: List -> Copy
        // ---------------------------------------------------------
        
        // Seed a Public Template (via Admin/System logic)
        // We need to switch tenant/role or use a service scope to seed this "foreign" public template
        Guid publicId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var pubBp = new WorkflowClassBlueprint 
            {
                 Events = new() { new EventBlueprint { EventId = "EVT-GLOBAL", Name = "Global" } },
                 StateMachine = new StateMachineBlueprint { InitialState = "S1", States = new() { "S1" } },
                 Workflow = new WorkflowBlueprint 
                 { 
                     StartStepId = "S1",
                     Steps = new() { new StepBlueprint { StepId = "S1", StepType = "End" } } 
                 }
            };
            var pubTmpl = new FlowOS.Domain.Entities.WorkflowClass(Guid.Empty, "GlobalTemplate", "1.0.0", pubBp);
            var manager = new FlowOS.Domain.Services.WorkflowClassManager();
            manager.Publish(pubTmpl);
            manager.SubmitForReview(pubTmpl);
            manager.ApproveAsPublic(pubTmpl);
            db.WorkflowClasses.Add(pubTmpl);
            await db.SaveChangesAsync();
            publicId = pubTmpl.Id;
        }

        // List Public (as Tenant)
        var listPubTmplResp = await _client.GetAsync("/api/workflow-classes?scope=Public");
        listPubTmplResp.EnsureSuccessStatusCode();
        var publicTemplates = await listPubTmplResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto[]>();
        Assert.Contains(publicTemplates, d => d.Id == publicId);

        // Copy
        var copyReq = new CopyWorkflowClassRequest { NewTenantId = _tenantId };
        var copyResp = await _client.PostAsJsonAsync($"/api/workflow-classes/{publicId}/copy", copyReq);
        copyResp.EnsureSuccessStatusCode();
        
        // Verify Copy Exists as Draft
        var copyResult = await copyResp.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();
        Assert.Equal("GlobalTemplate", copyResult.Name);
        Assert.Equal(WorkflowClassStatus.Draft, copyResult.Status);
        Assert.Equal(_tenantId, copyResult.TenantId);
        // ---------------------------------------------------------
        // 5. ERROR HANDLING: Create Invalid Draft
        // ---------------------------------------------------------
        var invalidDraftReq = new CreateWorkflowClassRequest
        {
            Name = null!, // Invalid
            Version = "0.1.0",
            Definition = new WorkflowClassBlueprint()
        };
        var invalidResp = await _client.PostAsJsonAsync("/api/workflow-classes", invalidDraftReq);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalidResp.StatusCode);
        
        var errorJson = await invalidResp.Content.ReadAsStringAsync();
        // Since we return BadRequest(new { Error = ex.Message }), the property is "Error" or "error" depending on serialization
        Assert.True(errorJson.Contains("Error", StringComparison.OrdinalIgnoreCase) || errorJson.Contains("error", StringComparison.OrdinalIgnoreCase));
    }
}
