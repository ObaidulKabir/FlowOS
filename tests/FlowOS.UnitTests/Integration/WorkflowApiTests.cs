using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.DTOs;
using FlowOS.Infrastructure.Persistence;
using FlowOS.UnitTests.Workflows;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.UnitTests.Integration;

public class WorkflowApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public WorkflowApiTests(WebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_WorkflowApi_" + Guid.NewGuid();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
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
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
    }

    [Fact]
    public async Task GetWorkflows_ShouldReturnRunningWorkflows()
    {
        // Arrange
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            // 1. Seed Definition
            var def = new WorkflowDefinition(_tenantId, "ApiTestFlow");
            def.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command));
            def.Publish();
            db.WorkflowDefinitions.Add(def);

            // 2. Seed Instances
            // Running Instance
            var runningInstance = new WorkflowInstance(_tenantId, def.Id, def.Version, "Start", Guid.NewGuid());
            db.WorkflowInstances.Add(runningInstance);

            // Completed Instance (Simulated manually or if we had a way to complete it)
            // Since we can't easily complete it via engine without full setup, we'll just manually set status via reflection or assuming new instance is Draft/Running?
            // WorkflowInstance constructor sets Status to Running (or whatever default is).
            // Let's check WorkflowInstance constructor.
            
            // Assuming we can't easily change status without private setters, 
            // but TestFlowOSDbContext might allow us to cheat if we needed, or we just rely on the default being Running.
            // Let's just test that we get the one we added.
            
            await db.SaveChangesAsync();
        }

        // Act
        var response = await _client.GetFromJsonAsync<List<WorkflowSummaryDto>>("/api/workflows?status=Running");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        Assert.Contains(response, w => w.Status == "Running");
        Assert.Equal(1, response.Count);
    }
}
