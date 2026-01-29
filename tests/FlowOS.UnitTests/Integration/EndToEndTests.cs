using System;
using System.Linq; // For SingleOrDefault
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

using FlowOS.UnitTests.Workflows; // For TestFlowOSDbContext

namespace FlowOS.UnitTests.Integration;

public class EndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.NewGuid();

    public EndToEndTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Instead of removing all descriptors (which might remove internal EF things),
                // we'll try to just Add the new one. Since it's the last one added, it should take precedence.
                // However, MediatR might have already captured the previous one? No, it resolves at runtime.
                
                // Let's remove ONLY the specific Context/Options
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);

                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                // Add simple InMemory DB using the TEST context but registered as the REAL context
                // Use Scoped lifetime explicitly to match EF Core default
                services.AddScoped<FlowOSDbContext>(provider => 
                {
                    var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                        .UseInMemoryDatabase("FlowOS_E2E_" + Guid.NewGuid())
                        .EnableSensitiveDataLogging()
                        .Options;
                    
                    return new TestFlowOSDbContext(options);
                });
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task StartWorkflow_ShouldReturnInstanceId()
    {
        // Arrange
        // We need to seed a WorkflowDefinition first
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var def = new WorkflowDefinition(_tenantId, "IntegrationTestFlow");
            def.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command));
            def.Publish();
            db.WorkflowDefinitions.Add(def);
            await db.SaveChangesAsync();

            // Act
            var command = new StartWorkflowCommand(
                _tenantId, 
                def.Id, 
                def.Version, 
                "Start", 
                Guid.NewGuid()
            );
            
            var response = await _client.PostAsJsonAsync("/api/workflows/start", command);

            // Assert
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<WorkflowStartResponse>();
            Assert.NotNull(result);
            Assert.NotEqual(Guid.Empty, result.WorkflowInstanceId);
        }
    }

    private record WorkflowStartResponse(Guid WorkflowInstanceId);
}
