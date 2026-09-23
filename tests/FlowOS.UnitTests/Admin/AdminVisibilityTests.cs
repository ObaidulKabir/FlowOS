using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Agents.Events;
using FlowOS.Application.Handlers;
using FlowOS.Application.Handlers.Admin;
using FlowOS.Application.Queries;
using FlowOS.Application.Queries.Admin;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

using System.Collections.Generic;
using FlowOS.Security.Policies;

namespace FlowOS.UnitTests.Admin;

public class StubPolicyProvider : IPolicyProvider
{
    public Task<IEnumerable<Policy>> GetApplicablePoliciesAsync(PolicyContext context) => 
        Task.FromResult<IEnumerable<Policy>>(new List<Policy>());
        
    public Task<IEnumerable<Policy>> GetAllPoliciesAsync() => 
        Task.FromResult<IEnumerable<Policy>>(new List<Policy>());
}

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
        var instance = new WorkflowInstance(tenantId, definition.Id, Guid.Empty, 1, "Step1", instanceId);
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
        var handler = new AdminQueryHandlers(new UnitOfWork(context), new StubPolicyProvider());
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

    [Fact]
    public async Task GetWorkflowDetail_Should_Include_Events_Correlated_By_Business_Id()
    {
        var context = GetInMemoryContext();
        var tenantId = Guid.NewGuid();
        var businessCorrelation = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "ExpenseApproval", 1);
        context.WorkflowDefinitions.Add(definition);

        var instance = new WorkflowInstance(tenantId, definition.Id, Guid.Empty, 1, "ManagerReview", businessCorrelation);
        context.WorkflowInstances.Add(instance);

        var started = new StandardEvent(tenantId, "WorkflowStarted");
        started.SetCorrelationId(instance.Id);
        started.AddMetadata("WorkflowName", "ExpenseApproval");
        started.AddMetadata("StartStep", "ManagerReview");
        context.Events.Add(started);

        var transition = new StandardEvent(tenantId, "EVT-APPROVE");
        transition.SetCorrelationId(businessCorrelation);
        transition.AddMetadata("FromState", "Draft");
        transition.AddMetadata("ToState", "Approved");
        context.Events.Add(transition);

        var foreign = new StandardEvent(Guid.NewGuid(), "EVT-FOREIGN");
        foreign.SetCorrelationId(instance.Id);
        context.Events.Add(foreign);

        await context.SaveChangesAsync();

        var handler = new AdminQueryHandlers(new UnitOfWork(context), new StubPolicyProvider());
        var result = await handler.Handle(new GetAdminWorkflowDetailQuery(instance.Id, tenantId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(instance.CurrentState, result.CurrentState);
        Assert.Equal(2, result.Timeline.Count);
        Assert.Contains(result.Timeline, t => t.EventType == "WorkflowStarted");
        Assert.Contains(result.Timeline, t => t.EventType == "EVT-APPROVE");
        Assert.DoesNotContain(result.Timeline, t => t.EventType == "EVT-FOREIGN");
    }

    [Fact]
    public async Task GetPublishedEvents_Should_Include_Instance_And_Business_Correlation()
    {
        var context = GetInMemoryContext();
        var tenantId = Guid.NewGuid();
        var businessCorrelation = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "ExpenseApproval", 1);
        context.WorkflowDefinitions.Add(definition);

        var instance = new WorkflowInstance(tenantId, definition.Id, Guid.Empty, 1, "ManagerReview", businessCorrelation);
        context.WorkflowInstances.Add(instance);

        var started = new StandardEvent(tenantId, "WorkflowStarted");
        started.SetCorrelationId(instance.Id);
        context.Events.Add(started);

        var transition = new StandardEvent(tenantId, "EVT-APPROVE");
        transition.SetCorrelationId(businessCorrelation);
        context.Events.Add(transition);

        await context.SaveChangesAsync();

        var handler = new GetPublishedEventsQueryHandler(new UnitOfWork(context));
        var events = await handler.Handle(
            new GetPublishedEventsQuery(tenantId, instance.Id, 50),
            CancellationToken.None);

        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.EventType == "WorkflowStarted");
        Assert.Contains(events, e => e.EventType == "EVT-APPROVE");
    }
}
