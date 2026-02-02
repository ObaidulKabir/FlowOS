using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Security;

public class Security_RoleCapability : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public Security_RoleCapability(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);
                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_Security_" + Guid.NewGuid();
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

    [Fact]
    public async Task Scenario_RoleBasedAccessControl()
    {
        // 1. Setup Data (Seeding Roles)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            // Define Roles
            // "Manager" has "workflow.start"
            if (!await db.Roles.AnyAsync(r => r.Name == "Manager"))
            {
                var managerRole = new Role(_tenantId, "Manager");
                managerRole.AddPermission("workflow.start");
                db.Roles.Add(managerRole);
            }

            // "Intern" has NO permissions
            if (!await db.Roles.AnyAsync(r => r.Name == "Intern"))
            {
                var internRole = new Role(_tenantId, "Intern");
                db.Roles.Add(internRole);
            }
            
            // Workflow Def needed to test Start
            var def = new FlowOS.Workflows.Domain.WorkflowDefinition(_tenantId, "SecureFlow");
            def.AddStep(new FlowOS.Workflows.Domain.WorkflowStepDefinition("Start", FlowOS.Workflows.Enums.WorkflowStepType.Command) { NextSteps = new() });
            def.Publish();
            db.WorkflowDefinitions.Add(def);
            
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Remove("x-tenant-id");
        _client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());

        // 2. Test: Intern tries to start workflow (Should Fail)
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Intern");
        
        var startCommand = new StartWorkflowCommand(_tenantId, null, "SecureFlow", 1, "Start", Guid.NewGuid());
        var responseIntern = await _client.PostAsJsonAsync("/api/workflows/start", startCommand);
        
        // Assert: 403 Forbidden or 500 with PolicyViolationException
        // The PolicyEnforcementBehavior throws PolicyViolationException.
        // The global exception handler should map this to 403.
        // Let's verify status code is NOT success.
        Assert.False(responseIntern.IsSuccessStatusCode, "Intern should NOT be able to start workflow");
        
        // Optional: Check error message
        var errorContent = await responseIntern.Content.ReadAsStringAsync();
        Assert.Contains("Missing required capability", errorContent);


        // 3. Test: Manager tries to start workflow (Should Succeed)
        _client.DefaultRequestHeaders.Remove("X-Mock-Role");
        _client.DefaultRequestHeaders.Add("X-Mock-Role", "Manager");
        
        var startCommand2 = new StartWorkflowCommand(_tenantId, null, "SecureFlow", 1, "Start", Guid.NewGuid());
        var responseManager = await _client.PostAsJsonAsync("/api/workflows/start", startCommand2);
        
        responseManager.EnsureSuccessStatusCode();
        
        // 4. Verify State
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var count = await db.WorkflowInstances.CountAsync();
            Assert.Equal(1, count); // Only Manager's attempt succeeded
        }
    }
}
