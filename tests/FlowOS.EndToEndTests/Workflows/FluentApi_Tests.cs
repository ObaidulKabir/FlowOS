using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Domain.Builders;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Builders;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Workflows;

public class FluentApi_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public FluentApi_Tests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);
                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);
                
                var dbName = "FlowOS_E2E_Fluent_" + Guid.NewGuid();
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
    public async Task Builders_Should_Create_Valid_Definitions()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();

            // 1. Event Builder
            var evt = EventBuilder.Create(_tenantId, "EVT-FLUENT")
                .WithName("Fluent Event")
                .WithDescription("Created via Builder")
                .ForEntity("Test")
                .AsCategory(EventCategory.System)
                .IsTerminal(false)
                .Build();
            
            evt.Publish();
            db.EventDefinitions.Add(evt);

            // 2. State Machine Builder
            var sm = StateMachineBuilder.Create(_tenantId, "FluentEntity", 1)
                .WithInitialState("Created")
                .AddTransition("Created", "Processed", "EVT-FLUENT")
                .AddState("Processed") // Optional if implicit, but good for clarity
                .Build();
            
            sm.Publish();
            db.StateMachineDefinitions.Add(sm);

            // 3. Workflow Builder
            var wf = WorkflowBuilder.Create(_tenantId, "FluentWorkflow", 1)
                .StartWith("Step1", WorkflowStepType.Command)
                    .Then("Step2")
                .AddStep("Step2", WorkflowStepType.HumanTask)
                    .On("EVT-FLUENT", "End")
                    .CompleteStep()
                .AddStep("End", WorkflowStepType.Command)
                .Build();
            
            wf.Publish();
            db.WorkflowDefinitions.Add(wf);

            await db.SaveChangesAsync();

            // Assert Persistence
            Assert.NotNull(await db.EventDefinitions.FirstOrDefaultAsync(e => e.EventId == "EVT-FLUENT"));
            Assert.NotNull(await db.StateMachineDefinitions.FirstOrDefaultAsync(s => s.EntityType == "FluentEntity"));
            Assert.NotNull(await db.WorkflowDefinitions.FirstOrDefaultAsync(w => w.Name == "FluentWorkflow"));
            
            // Assert Structure
            var savedWf = await db.WorkflowDefinitions.FirstAsync(w => w.Name == "FluentWorkflow");
            Assert.Equal(3, savedWf.Steps.Count);
            Assert.Equal("Step2", savedWf.Steps.First(s => s.StepId == "Step1").NextSteps["Default"]);
        }
    }
}
