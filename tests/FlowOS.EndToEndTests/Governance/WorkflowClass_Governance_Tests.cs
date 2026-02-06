using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Governance;

public class WorkflowClass_Governance_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public WorkflowClass_Governance_Tests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);
                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_Governance_" + Guid.NewGuid();
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
    }

    [Fact]
    public async Task Validation_Prevents_Publishing_Invalid_WorkflowClass()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<WorkflowClassManager>();
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();

            // 1. Create Invalid Draft (Missing Name, Invalid Transition)
            var bp = new WorkflowClassBlueprint
            {
                // Missing Events definition for used transition
                StateMachine = new StateMachineBlueprint
                {
                    InitialState = "Draft",
                    States = new() { "Draft", "Published" },
                    Transitions = new() 
                    { 
                        new TransitionBlueprint { FromState = "Draft", ToState = "Published", EventId = "EVT-PUBLISH" } 
                    }
                }
            };

            var wc = new WorkflowClass(_tenantId, "InvalidWC", "1.0.0", bp);
            db.WorkflowClasses.Add(wc);
            await db.SaveChangesAsync();

            // 2. Try Publish
            var result = manager.Publish(wc);

            // 3. Assert Failure
            Assert.False(result.IsValid, "Should fail validation");
            Assert.Contains(result.Errors, e => e.Category == "Consistency");
            Assert.Equal(WorkflowClassStatus.Draft, wc.Status); // Status should remain Draft
        }
    }

    [Fact]
    public async Task Scope_Lifecycle_Is_Enforced()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<WorkflowClassManager>();
            
            // 1. Create Valid Draft
            var bp = new WorkflowClassBlueprint
            {
                Events = new() { new EventBlueprint { EventId = "EVT-GO", Name = "Go" } },
                StateMachine = new StateMachineBlueprint 
                { 
                    InitialState = "Start", States = new() { "Start", "End" }, 
                    Transitions = new() { new TransitionBlueprint { FromState = "Start", ToState = "End", EventId = "EVT-GO" } } 
                },
                Workflow = new WorkflowBlueprint 
                { 
                    StartStepId = "Start",
                    Steps = new() 
                    { 
                        new StepBlueprint { StepId = "Start", StepType = "Command", NextSteps = new() { { "EVT-GO", "End" } } },
                        new StepBlueprint { StepId = "End", StepType = "End" }
                    } 
                }
            };

            var wc = new WorkflowClass(_tenantId, "ValidWC", "1.0.0", bp);
            
            // 2. Publish (Draft -> Private)
            var pubResult = manager.Publish(wc);
            Assert.True(pubResult.IsValid);
            Assert.Equal(WorkflowClassStatus.Published, wc.Status);
            Assert.Equal(WorkflowClassScope.Private, wc.Scope);

            // 3. Submit (Private -> Shared)
            var subResult = manager.SubmitForReview(wc);
            Assert.True(subResult.IsValid);
            Assert.Equal(WorkflowClassStatus.Shared, wc.Status);
            Assert.Equal(WorkflowClassScope.Shared, wc.Scope);

            // 4. Approve (Shared -> Public)
            var appResult = manager.ApproveAsPublic(wc);
            Assert.True(appResult.IsValid);
            Assert.Equal(WorkflowClassStatus.Public, wc.Status);
            Assert.Equal(WorkflowClassScope.Public, wc.Scope);
        }
    }

    [Fact]
    public async Task Public_WorkflowClass_Can_Be_Copied_To_Tenant()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            // Setup Public Template
            var bp = new WorkflowClassBlueprint { /* Valid Blueprint */ }; // Minimal valid
            // Re-use valid logic
             var bpValid = new WorkflowClassBlueprint
            {
                Events = new() { new EventBlueprint { EventId = "EVT-GO", Name = "Go" } },
                StateMachine = new StateMachineBlueprint 
                { 
                    InitialState = "Start", States = new() { "Start", "End" }, 
                    Transitions = new() { new TransitionBlueprint { FromState = "Start", ToState = "End", EventId = "EVT-GO" } } 
                },
                Workflow = new WorkflowBlueprint 
                { 
                    StartStepId = "Start",
                    Steps = new() 
                    { 
                        new StepBlueprint { StepId = "Start", StepType = "Command", NextSteps = new() { { "EVT-GO", "End" } } },
                        new StepBlueprint { StepId = "End", StepType = "End" }
                    } 
                }
            };

            var template = new WorkflowClass(Guid.Empty, "PublicTemplate", "1.0.0", bpValid);
            // Force status to Public via Reflection or just run the lifecycle (cleaner)
            template.Publish();
            template.SubmitForReview();
            template.ApproveAsPublic();

            // Copy to Tenant
            var newTenantId = Guid.NewGuid();
            var copy = template.CreateCopyForTenant(newTenantId);

            // Assert Copy
            Assert.Equal(newTenantId, copy.TenantId);
            Assert.Equal("1.0.0", copy.Version);
            Assert.Equal(WorkflowClassStatus.Draft, copy.Status);
            Assert.Equal(WorkflowClassScope.Private, copy.Scope);
            
            // Assert Blueprint Content
            Assert.Equal("PublicTemplate", copy.Name);
            Assert.Equal("EVT-GO", copy.Definition.Events[0].EventId);
        }
    }
}
