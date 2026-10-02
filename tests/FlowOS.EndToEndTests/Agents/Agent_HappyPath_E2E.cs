using System.Net.Http.Json;
using System.Text.Json;
using FlowOS.Application.Services;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FlowOS.Agents;
using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Commands;
using Xunit;
using MediatR;
namespace FlowOS.EndToEndTests.Agents;

public class Agent_HappyPath_E2E : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public Agent_HappyPath_E2E(WebApplicationFactory<Program> factory)
    {
        var databaseName = "FlowOS_Agent_E2E_" + Guid.NewGuid();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var hostedServices = services.Where(s => s.ServiceType == typeof(IHostedService) || 
                    s.ImplementationType?.Name == "AgentTaskProcessorService").ToList();
                foreach (var service in hostedServices)
                {
                    services.Remove(service);
                }
            });

            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(x =>
                             x.ServiceType == typeof(FlowOSDbContext) ||
                             x.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList())
                {
                    services.Remove(descriptor);
                }

                services.AddScoped<FlowOSDbContext>(_ =>
                {
                    var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                        .UseInMemoryDatabase(databaseName)
                        .Options;
                    return new TestFlowOSDbContext(options);
                });

                // Inject our mock factory so the real runner executes but with our deterministic agent
                var factoryDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IWorkflowAgentFactory));
                if (factoryDescriptor != null) services.Remove(factoryDescriptor);
                services.AddScoped<IWorkflowAgentFactory, MockAgentFactory>();
            });
        });
    }

    [Fact]
    public async Task Agent_Task_Automatically_Advances_Workflow()
    {
        Guid workflowInstanceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            
            var workflowClass = new WorkflowClass(
                _tenantId,
                "AgentTestClass",
                "1.0.0",
                new WorkflowClassBlueprint
                {
                    Events =
                    [
                        new EventBlueprint
                        {
                            EventId = "EVT_ADVANCE",
                            Name = "Advance",
                            Category = EventCategory.Agent
                        },
                        new EventBlueprint
                        {
                            EventId = "EVT_COMPLETE",
                            Name = "Complete",
                            Category = EventCategory.Human
                        }
                    ],
                    StateMachine = new StateMachineBlueprint
                    {
                        EntityType = "AgentSubject",
                        InitialState = "Draft",
                        States = ["Draft", "Pending", "Completed"],
                        Transitions =
                        [
                            new TransitionBlueprint
                            {
                                FromState = "Draft",
                                ToState = "Pending",
                                EventId = "EVT_ADVANCE"
                            },
                            new TransitionBlueprint
                            {
                                FromState = "Pending",
                                ToState = "Completed",
                                EventId = "EVT_COMPLETE"
                            }
                        ]
                    },
                    Workflow = new WorkflowBlueprint
                    {
                        StartStepId = "Step1",
                        Steps =
                        [
                            new StepBlueprint
                            {
                                StepId = "Step1",
                                StepType = "Command",
                                Actor = "Agent",
                                AutoCommit = new StepAutoCommitBlueprint { MinConfidence = 0.5, AllowedEvents = ["EVT_ADVANCE"] },
                                RequiredRoles = ["AI Assistant"],
                                AgentPrompt = "test-prompt",
                                NextSteps = new Dictionary<string, string> { ["EVT_ADVANCE"] = "Step2" }
                            },
                            new StepBlueprint
                            {
                                StepId = "Step2",
                                StepType = "HumanTask",
                                RequiredRoles = ["Admin"],
                                NextSteps = new Dictionary<string, string> { ["EVT_COMPLETE"] = "END" }
                            }
                        ]
                    }
                }
            );
            
            db.WorkflowClasses.Add(workflowClass);

            var admin = new Role(_tenantId, "Admin");
            admin.AddPermission("event.publish");
            admin.AddPermission("workflow.start");
            admin.AddPermission("event.publish.EVT_ADVANCE");
            db.Roles.Add(admin);
            
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new FlowOS.Application.Commands.Governance.PublishWorkflowClassCommand(_tenantId, workflowClass.Id));

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("x-tenant-id", _tenantId.ToString());
            client.DefaultRequestHeaders.Add("X-Mock-Role", "Admin");
            client.DefaultRequestHeaders.Add("X-Mock-UserId", "admin1");

            var startResponse = await client.PostAsJsonAsync("/api/workflows/start", new StartWorkflowCommand(_tenantId, WorkflowClassId: workflowClass.Id));
            var startBody = await startResponse.Content.ReadAsStringAsync();
            Assert.True(startResponse.IsSuccessStatusCode, $"Start failed: {startResponse.StatusCode} - {startBody}");
            
            var startResult = JsonDocument.Parse(startBody);
            workflowInstanceId = startResult.RootElement.GetProperty("workflowInstanceId").GetGuid();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var coordinator = scope.ServiceProvider.GetRequiredService<IAgentTaskCoordinator>();
            var queue = scope.ServiceProvider.GetRequiredService<IAgentTaskQueue>();
            
            var claims = await queue.ClaimBatchAsync("test-worker", 1, TimeSpan.FromMinutes(1), default);
            Assert.Single(claims);
            
            var claim = claims[0];
            await coordinator.ProcessClaimAsync(claim, default);
            
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var instance = await db.WorkflowInstances.FirstAsync(x => x.Id == workflowInstanceId);
            
            Assert.Equal("Step2", instance.CurrentStepId);
        }
    }

    public class MockAgentFactory : IWorkflowAgentFactory
    {
        public Task<IWorkflowAgent> CreateAsync(
            DecisionPacket packet,
            string? requestedAgentId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IWorkflowAgent>(new MockAgent());
        }
    }

    public class MockAgent : IWorkflowAgent
    {
        public Task<AgentResult> ExecuteAsync(AgentContext context)
        {
            return Task.FromResult(AgentResult.WithActions(
                "Test insight",
                new List<SuggestedAction> 
                { 
                    new SuggestedAction("EVT_ADVANCE", "Test reason", 1.0, new Dictionary<string, object>())
                }));
        }
    }
}
