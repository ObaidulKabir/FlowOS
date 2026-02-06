using System;
using System.Linq;
using System.Net;
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

public class TenantDashboard_Negative_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public TenantDashboard_Negative_Tests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);
                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_Dashboard_Negative_" + Guid.NewGuid();
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

    private async Task<WorkflowClassResponseDto> CreateDraftAsync(string name = "Draft")
    {
        var draftReq = new CreateWorkflowClassRequest
        {
            Name = name,
            Version = "0.1.0",
            Definition = new WorkflowClassBlueprint
            {
                Events = new() { new EventBlueprint { EventId = "EVT-1", Name = "E1" } },
                StateMachine = new StateMachineBlueprint { InitialState = "S1", States = new() { "S1" } },
                Workflow = new WorkflowBlueprint 
                { 
                    StartStepId = "S1",
                    Steps = new() { new StepBlueprint { StepId = "S1", StepType = "End" } } 
                }
            }
        };
        var resp = await _client.PostAsJsonAsync("/api/workflow-classes", draftReq);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<WorkflowClassResponseDto>();
    }

    [Fact]
    public async Task Cannot_Delete_Published_WorkflowClass()
    {
        SetupTenantHeaders();
        
        // 1. Create & Publish
        var wc = await CreateDraftAsync("ToDelete");
        var pubResp = await _client.PostAsync($"/api/workflow-classes/{wc.Id}/publish", null);
        pubResp.EnsureSuccessStatusCode();

        // 2. Attempt Delete
        var delResp = await _client.DeleteAsync($"/api/workflow-classes/{wc.Id}");
        
        // 3. Assert Failure (BadRequest)
        Assert.Equal(HttpStatusCode.BadRequest, delResp.StatusCode);
        var msg = await delResp.Content.ReadAsStringAsync();
        Assert.Contains("Only Drafts can be hard deleted", msg);
    }

    [Fact]
    public async Task Cannot_Withdraw_Published_Or_Draft()
    {
        SetupTenantHeaders();

        // Draft
        var draft = await CreateDraftAsync("ToWithdraw");
        var withDraftResp = await _client.PostAsync($"/api/workflow-classes/{draft.Id}/withdraw", null);
        Assert.Equal(HttpStatusCode.BadRequest, withDraftResp.StatusCode);

        // Published
        await _client.PostAsync($"/api/workflow-classes/{draft.Id}/publish", null);
        var withPubResp = await _client.PostAsync($"/api/workflow-classes/{draft.Id}/withdraw", null);
        Assert.Equal(HttpStatusCode.BadRequest, withPubResp.StatusCode);
    }

    [Fact]
    public async Task Cannot_Copy_Private_WorkflowClass()
    {
        SetupTenantHeaders();
        
        // Create Private (Published)
        var wc = await CreateDraftAsync("PrivateToCopy");
        await _client.PostAsync($"/api/workflow-classes/{wc.Id}/publish", null);

        // Attempt Copy
        var copyReq = new CopyWorkflowClassRequest { NewTenantId = _tenantId };
        var copyResp = await _client.PostAsJsonAsync($"/api/workflow-classes/{wc.Id}/copy", copyReq);

        // Assert Failure (BadRequest - "Only Public can be copied")
        Assert.Equal(HttpStatusCode.BadRequest, copyResp.StatusCode);
        var msg = await copyResp.Content.ReadAsStringAsync();
        Assert.Contains("Only Public WorkflowClasses can be copied", msg);
    }
}
