using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Agents.Events;
using FlowOS.Application.Common.Models;
using FlowOS.Application.DTOs;
using FlowOS.Application.EventHandlers;
using FlowOS.Application.Handlers;
using FlowOS.Application.Queries;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

using FlowOS.Infrastructure.Services;
using FlowOS.Core.Interfaces;
using Moq;
using Microsoft.Extensions.Logging;

namespace FlowOS.UnitTests.Integration;

public class TaskApiTests
{
    private FlowOSDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FlowOSDbContext(options);
    }
    
    private IEventRegistry GetMockRegistry(FlowOSDbContext context)
    {
        return new EventRegistry(context, new Mock<ILogger<EventRegistry>>().Object);
    }

    [Fact]
    public async Task GetTask_Should_Return_Insights_From_Projection()
    {
        // Arrange
        var context = GetInMemoryContext();
        var tenantId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var instanceId = Guid.NewGuid(); // Use as TaskId

        // 1. Seed Workflow (Task)
        // Need to use reflection or constructor if public. 
        // WorkflowInstance ctor is public.
        var instance = new WorkflowInstance(tenantId, Guid.NewGuid(), 1, "ReviewStep", instanceId); // instanceId passed as correlationId
        instance.Wait(); // Set to Waiting
        context.WorkflowInstances.Add(instance);
        await context.SaveChangesAsync();

        // 2. Simulate Agent Insight Event -> Projector
        var projector = new AgentInsightProjector(context);
        var insightEvent = new AgentInsightGenerated(tenantId, "agent-001", "Risk is high", "Risk Assessment");
        insightEvent.SetCorrelationId(instance.Id); // Correlation to workflow ID (which is the Task ID)

        await projector.Handle(new DomainEventNotification<AgentInsightGenerated>(insightEvent), CancellationToken.None);

        // Act
        var handler = new TaskQueryHandlers(context);
        var result = await handler.Handle(new GetTaskByIdQuery(instance.Id), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(instance.Id, result.TaskId);
        Assert.Single(result.AgentInsights);
        Assert.Equal("Risk is high", result.AgentInsights[0].Insight);
    }

    [Fact]
    public async Task CompleteTask_Should_Emit_Event_And_Not_Advance_Directly()
    {
        // Arrange
        var context = GetInMemoryContext();
        var tenantId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        
        // Correctly create definition
        var definition = new WorkflowDefinition(tenantId, "TestFlow", 1);
        definition.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command));
        
        var reviewStep = new WorkflowStepDefinition("ReviewStep", WorkflowStepType.HumanTask);
        reviewStep.NextSteps.Add("TaskCompleted", "EndStep");
        definition.AddStep(reviewStep);
        
        definition.AddStep(new WorkflowStepDefinition("EndStep", WorkflowStepType.Command));
        definition.Publish();
        
        context.WorkflowDefinitions.Add(definition);

        var correlationId = Guid.NewGuid();
        var instance = new WorkflowInstance(tenantId, definition.Id, 1, "ReviewStep", correlationId);
        context.WorkflowInstances.Add(instance);
        await context.SaveChangesAsync();

        var handler = new WorkflowCommandHandlers(context, GetMockRegistry(context)); // Uses real Engine, but no transitions defined for this dummy definition

        // Act
        var command = new Application.Commands.CompleteTaskCommand(tenantId, instance.Id, instance.Id);
        var success = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(success);
        
        // Verify Event Emitted
        var events = await context.Events.ToListAsync();
        var taskCompleted = Assert.Single(events, e => e.EventType == "TaskCompleted");
        Assert.IsType<TaskCompleted>(taskCompleted);
        Assert.Equal(instance.Id, ((TaskCompleted)taskCompleted).TaskId);

        // Verify Workflow State (Should HAVE advanced because Engine processed the event)
        var updatedInstance = await context.WorkflowInstances.FindAsync(instance.Id);
        Assert.Equal("EndStep", updatedInstance.CurrentStepId);
    }
}
