using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Agents.Events;
using FlowOS.Application.Handlers.Admin;
using FlowOS.Application.Queries.Admin;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowOS.UnitTests.Admin;

public class AdminVisibilityTests
{
    private FlowOSDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FlowOSDbContext(options);
    }

    [Fact]
    public async Task GetWorkflowDetail_Should_Return_Curated_Timeline()
    {
        // Arrange
        var context = GetInMemoryContext();
        var tenantId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        // 1. Definition
        var definition = new WorkflowDefinition(tenantId, "TestFlow", 1);
        context.WorkflowDefinitions.Add(definition);

        // 2. Instance
        var instance = new WorkflowInstance(tenantId, definition.Id, 1, "Step1", instanceId);
        context.WorkflowInstances.Add(instance);

        // 3. Events (Timeline)
        // A. Agent Insight
        var agentEvent = new AgentInsightGenerated(tenantId, "TraeAI", "Looks good", "Analysis");
        agentEvent.SetCorrelationId(instance.Id); // Use instance.Id
        context.Events.Add(agentEvent);

        // B. Task Completion
        var taskEvent = new TaskCompleted(tenantId, Guid.NewGuid(), Guid.NewGuid());
        taskEvent.SetCorrelationId(instance.Id); // Use instance.Id
        context.Events.Add(taskEvent);

        await context.SaveChangesAsync();

        // Act
        var handler = new AdminQueryHandlers(context);
        // Ensure tenantId matches (in test, we used same tenantId)
        var result = await handler.Handle(new GetAdminWorkflowDetailQuery(instance.Id, tenantId), CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestFlow", result.DefinitionName);
        Assert.Equal(2, result.Timeline.Count);

        // Verify "Curated" descriptions
        var agentItem = result.Timeline.Find(t => t.EventType == "AgentInsightGenerated");
        Assert.Contains("TraeAI suggested: Looks good", agentItem.Summary);

        var taskItem = result.Timeline.Find(t => t.EventType == "TaskCompleted");
        Assert.Contains("Task completed by user", taskItem.Summary);
    }
}
