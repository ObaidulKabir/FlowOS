using FlowOS.Application.Commands;
using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Handlers;
using FlowOS.Application.Queries;
using FlowOS.Application.Services;
using FlowOS.Core.Interfaces;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using FlowOS.Security.Interfaces;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FlowOS.UnitTests.Workflows;

/// <summary>
/// A person can be named on a live instance, and later actions can say which person is acting
/// when the trusted caller is a shared tenant API key.
/// </summary>
public class InstanceRoleAssignmentTests : IDisposable
{
    private readonly FlowOSDbContext _context;
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICapabilityService> _capabilities = new();
    private readonly Mock<IEventRegistry> _events = new();
    private readonly WorkflowCommandHandlers _handler;

    public InstanceRoleAssignmentTests()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new FlowOSDbContext(options);
        _currentUser.Setup(x => x.Id).Returns("api-key");
        _currentUser.Setup(x => x.Roles).Returns(new List<string>());
        _capabilities
            .Setup(x => x.GetCapabilitiesAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "event.publish" });

        _handler = new WorkflowCommandHandlers(
            new UnitOfWork(_context),
            _events.Object,
            _currentUser.Object,
            _capabilities.Object,
            new WorkflowEngine(new StateMachineEngine()),
            businessRoleResolver: new BusinessRoleResolver());
    }

    [Fact]
    public async Task Assign_AfterStart_PersistsAndASecondCallReplacesThePerson()
    {
        var (tenantId, instance) = await SeedAsync(resolutionType: "Assignment");

        Assert.True(await _handler.Handle(
            new AssignInstanceRoleCommand(tenantId, instance.Id, "Sales", "user-123"),
            CancellationToken.None));

        _context.ChangeTracker.Clear();
        var first = await _context.WorkflowInstances.FindAsync(instance.Id);
        Assert.Equal("user-123", first!.RoleAssignments["Sales"]);

        Assert.True(await _handler.Handle(
            new AssignInstanceRoleCommand(tenantId, instance.Id, "sales", "user-456"),
            CancellationToken.None));

        _context.ChangeTracker.Clear();
        var replaced = await _context.WorkflowInstances.FindAsync(instance.Id);
        Assert.Equal("user-456", replaced!.RoleAssignments["Sales"]);
    }

    [Fact]
    public async Task Assign_UnknownRole_IsRejected()
    {
        var (tenantId, instance) = await SeedAsync(resolutionType: "Assignment");

        var error = await Assert.ThrowsAsync<ArgumentException>(() => _handler.Handle(
            new AssignInstanceRoleCommand(tenantId, instance.Id, "Warehouse", "user-123"),
            CancellationToken.None));

        Assert.Contains("not a business role", error.Message);
    }

    [Theory]
    [InlineData("Expression")]
    [InlineData("Static")]
    public async Task Assign_NonAssignmentRole_IsRejected(string resolutionType)
    {
        var (tenantId, instance) = await SeedAsync(resolutionType);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => _handler.Handle(
            new AssignInstanceRoleCommand(tenantId, instance.Id, "Sales", "user-123"),
            CancellationToken.None));

        Assert.Contains("Assignment", error.Message);
    }

    [Fact]
    public async Task Assign_CompletedInstance_IsRejected()
    {
        var (tenantId, instance) = await SeedAsync(resolutionType: "Assignment");
        instance.Complete();
        await _context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => _handler.Handle(
            new AssignInstanceRoleCommand(tenantId, instance.Id, "Sales", "user-123"),
            CancellationToken.None));

        Assert.Contains("Completed", error.Message);
    }

    [Fact]
    public async Task Publish_WithMatchingActor_Advances_AndADifferentActorIsDenied()
    {
        var (tenantId, instance) = await SeedAsync(resolutionType: "Assignment", eventKey: "QuoteViewed");
        await _handler.Handle(
            new AssignInstanceRoleCommand(tenantId, instance.Id, "Sales", "user-123"),
            CancellationToken.None);

        var allowed = await _handler.Handle(
            new PublishEventCommand(tenantId, instance.Id, "QuoteViewed", ActorId: "user-123"),
            CancellationToken.None);

        Assert.True(allowed);
        _context.ChangeTracker.Clear();
        var advanced = await _context.WorkflowInstances.FindAsync(instance.Id);
        Assert.Equal(WorkflowInstanceStatus.Completed, advanced!.Status);

        var (otherTenant, other) = await SeedAsync(resolutionType: "Assignment", eventKey: "QuoteViewed");
        await _handler.Handle(
            new AssignInstanceRoleCommand(otherTenant, other.Id, "Sales", "user-123"),
            CancellationToken.None);

        var denied = await Assert.ThrowsAsync<PolicyViolationException>(() => _handler.Handle(
            new PublishEventCommand(otherTenant, other.Id, "QuoteViewed", ActorId: "user-456"),
            CancellationToken.None));
        Assert.Equal("ContextEventRole", denied.PolicyName);
    }

    [Fact]
    public async Task Publish_OtherRoleThatGrantsTheCapability_CannotActForTheWaitingRole()
    {
        var (tenantId, instance) = await SeedSalesAndManagerAsync();
        await _handler.Handle(new AssignInstanceRoleCommand(tenantId, instance.Id, "Sales", "user-123"), CancellationToken.None);
        await _handler.Handle(new AssignInstanceRoleCommand(tenantId, instance.Id, "Manager", "user-456"), CancellationToken.None);

        var denied = await Assert.ThrowsAsync<PolicyViolationException>(() => _handler.Handle(
            new PublishEventCommand(tenantId, instance.Id, "QuoteViewed", ActorId: "user-456"),
            CancellationToken.None));
        Assert.Equal("ContextEventRole", denied.PolicyName);

        var allowed = await _handler.Handle(
            new PublishEventCommand(tenantId, instance.Id, "QuoteViewed", ActorId: "user-123"),
            CancellationToken.None);
        Assert.True(allowed);
    }

    [Fact]
    public async Task Publish_WhenRoleGrantsNoCapability_StillRequiresTheAssignedPerson()
    {
        var (tenantId, instance) = await SeedAsync(resolutionType: "Assignment", eventKey: "QuoteViewed", grantCapability: false);
        await _handler.Handle(new AssignInstanceRoleCommand(tenantId, instance.Id, "Sales", "user-123"), CancellationToken.None);

        var denied = await Assert.ThrowsAsync<PolicyViolationException>(() => _handler.Handle(
            new PublishEventCommand(tenantId, instance.Id, "QuoteViewed", ActorId: "user-456"),
            CancellationToken.None));
        Assert.Equal("ContextEventRole", denied.PolicyName);

        Assert.True(await _handler.Handle(
            new PublishEventCommand(tenantId, instance.Id, "QuoteViewed", ActorId: "user-123"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Publish_WithNoStepRoles_StillRequiresARoleThatGrantsTheEvent()
    {
        var (tenantId, instance) = await SeedSalesAndManagerAsync(stepListsSales: false);
        await _handler.Handle(new AssignInstanceRoleCommand(tenantId, instance.Id, "Manager", "user-456"), CancellationToken.None);

        Assert.True(await _handler.Handle(
            new PublishEventCommand(tenantId, instance.Id, "QuoteViewed", ActorId: "user-456"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Publish_TimerAndAgentCommit_AreNotPersonChecks()
    {
        var (tenantId, instance) = await SeedAsync(resolutionType: "Assignment", eventKey: "QuoteViewed");
        await _handler.Handle(new AssignInstanceRoleCommand(tenantId, instance.Id, "Sales", "user-123"), CancellationToken.None);

        var timerUser = new Mock<ICurrentUser>();
        timerUser.Setup(x => x.Id).Returns(string.Empty);
        timerUser.Setup(x => x.Roles).Returns(new List<string>());
        var timerHandler = new WorkflowCommandHandlers(
            new UnitOfWork(_context),
            _events.Object,
            timerUser.Object,
            _capabilities.Object,
            new WorkflowEngine(new StateMachineEngine()),
            businessRoleResolver: new BusinessRoleResolver());

        Assert.True(await timerHandler.Handle(
            new PublishEventCommand(tenantId, instance.Id, "QuoteViewed"),
            CancellationToken.None));

        var (agentTenant, agentInstance) = await SeedAsync(resolutionType: "Assignment", eventKey: "QuoteViewed");
        await _handler.Handle(new AssignInstanceRoleCommand(agentTenant, agentInstance.Id, "Sales", "user-123"), CancellationToken.None);
        Assert.True(await _handler.Handle(
            new PublishEventCommand(agentTenant, agentInstance.Id, "QuoteViewed", ActorId: "Agent:quote-agent"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Publish_WithoutActor_OnSharedKey_DoesNotMatchTheAssignee()
    {
        var (tenantId, instance) = await SeedAsync(resolutionType: "Assignment", eventKey: "QuoteViewed");
        await _handler.Handle(
            new AssignInstanceRoleCommand(tenantId, instance.Id, "Sales", "user-123"),
            CancellationToken.None);

        var denied = await Assert.ThrowsAsync<PolicyViolationException>(() => _handler.Handle(
            new PublishEventCommand(tenantId, instance.Id, "QuoteViewed"),
            CancellationToken.None));

        Assert.Equal("ContextEventRole", denied.PolicyName);
    }

    [Fact]
    public async Task CompleteTask_UsesActorId_AgainstTheAssignedPerson()
    {
        var (tenantId, instance) = await SeedAsync(resolutionType: "Assignment", humanTask: true);
        await _handler.Handle(
            new AssignInstanceRoleCommand(tenantId, instance.Id, "Sales", "user-123"),
            CancellationToken.None);

        var denied = await Assert.ThrowsAsync<PolicyViolationException>(() => _handler.Handle(
            new CompleteTaskCommand(tenantId, instance.Id, Guid.NewGuid()),
            CancellationToken.None));
        Assert.Equal("ContextTaskRole", denied.PolicyName);

        var allowed = await _handler.Handle(
            new CompleteTaskCommand(tenantId, instance.Id, Guid.NewGuid(), ActorId: "user-123"),
            CancellationToken.None);

        Assert.True(allowed);
        _context.ChangeTracker.Clear();
        var completed = await _context.WorkflowInstances.FindAsync(instance.Id);
        Assert.Equal(WorkflowInstanceStatus.Completed, completed!.Status);
    }

    [Fact]
    public void Agents_AreToldToAssignAfterStart_AndPassActorId()
    {
        var instructions = FlowOsMcpGuidance.SystemInstructions;
        Assert.Contains("assign_instance_role", instructions);
        Assert.Contains("actorId", instructions);
        Assert.Contains("roleAssignments", McpToolDescriptions.For("start_workflow"));
        Assert.Contains("workflow.start", McpToolDescriptions.RequiredCapabilitiesFor("assign_instance_role"));
        Assert.Equal("irreversible", McpToolDescriptions.ProfileFor("assign_instance_role").SideEffect);
        Assert.Contains("AllowedRoles", McpToolDescriptions.For("publish_event"));
        Assert.Contains("roleAssignments", McpToolDescriptions.For("get_workflow_instance_status"));
        Assert.Contains("roleAssignments", McpToolDescriptions.For("list_workflow_instances"));
        Assert.Contains("ActorId", McpToolDescriptions.For("get_workflow_history"));
        Assert.Contains("AllowedRoles", McpToolSchemas.PublishEvent().ToString());
        Assert.Contains("ActorId", McpToolSchemas.CompleteTask().ToString());
        Assert.True(TenantEntitlementPolicy.McpToolRequiresPaidPlan("assign_instance_role", true, "irreversible"));
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task StatusAndInbox_ReturnTheAssignedPerson()
    {
        var (tenantId, instance) = await SeedAsync(resolutionType: "Assignment", humanTask: true);
        instance.Wait();
        await _context.SaveChangesAsync();
        await _handler.Handle(new AssignInstanceRoleCommand(tenantId, instance.Id, "Sales", "user-123"), CancellationToken.None);

        _context.ChangeTracker.Clear();
        var summary = await new WorkflowInstanceRepository(_context).GetSummaryByIdAsync(instance.Id, tenantId);
        Assert.Equal("user-123", summary!.RoleAssignments["Sales"]);

        var task = Assert.Single(await new TaskQueryHandlers(new UnitOfWork(_context)).Handle(new GetTasksQuery
        {
            TenantId = tenantId,
            CallerRoles = ["Sales"]
        }, CancellationToken.None));
        Assert.Equal("user-123", task.AssigneeId);
        Assert.Equal("user-123", task.RoleAssignments["Sales"]);
    }

    [Fact]
    public async Task History_ReturnsAssignments_AndThePersonWhoCompletedTheTask()
    {
        var (tenantId, instance) = await SeedAsync(resolutionType: "Assignment", humanTask: true);
        await _handler.Handle(new AssignInstanceRoleCommand(tenantId, instance.Id, "Sales", "user-123"), CancellationToken.None);
        Assert.True(await _handler.Handle(
            new CompleteTaskCommand(tenantId, instance.Id, Guid.NewGuid(), ActorId: "user-123"),
            CancellationToken.None));

        _context.ChangeTracker.Clear();
        var detail = await new FlowOS.Application.Handlers.Admin.AdminQueryHandlers(
            new UnitOfWork(_context),
            new Mock<FlowOS.Security.Policies.IPolicyProvider>().Object)
            .Handle(new FlowOS.Application.Queries.Admin.GetAdminWorkflowDetailQuery(instance.Id, tenantId), CancellationToken.None);

        Assert.Equal("user-123", detail.RoleAssignments["Sales"]);
        var completed = Assert.Single(detail.Timeline, item => item.EventType == "TaskCompleted");
        Assert.Equal("user-123", completed.KeyData["ActorId"]);
    }

    private async Task<(Guid TenantId, WorkflowInstance Instance)> SeedAsync(
        string resolutionType,
        string eventKey = "QuoteViewed",
        bool humanTask = false,
        bool grantCapability = true)
    {
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "QuoteSla", 1, "Review");
        definition.AddStep(new WorkflowStepDefinition(
            "Review",
            humanTask ? WorkflowStepType.HumanTask : WorkflowStepType.Command)
        {
            AllowedRoles = ["Sales"],
            NextSteps = new Dictionary<string, string>
            {
                [humanTask ? "TaskCompleted" : eventKey] = "END"
            }
        });
        definition.AddStep(new WorkflowStepDefinition("END", WorkflowStepType.End));
        definition.AttachBusinessRoles(
        [
            new BusinessRoleDefinition
            {
                Name = "Sales",
                ResolutionType = resolutionType,
                Capabilities = grantCapability ? ["event.publish"] : [],
                MemberExpression = resolutionType == "Expression" ? "{{assigneeId}}" : null,
                StaticMembers = resolutionType == "Static" ? ["static-user"] : []
            }
        ]);
        definition.Publish();

        var instance = new WorkflowInstance(tenantId, definition.Id, Guid.NewGuid(), 1, "Review");
        _context.WorkflowDefinitions.Add(definition);
        _context.WorkflowInstances.Add(instance);
        await _context.SaveChangesAsync();
        return (tenantId, instance);
    }

    private async Task<(Guid TenantId, WorkflowInstance Instance)> SeedSalesAndManagerAsync(bool stepListsSales = true)
    {
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "QuoteSla", 1, "Review");
        definition.AddStep(new WorkflowStepDefinition("Review", WorkflowStepType.Command)
        {
            AllowedRoles = stepListsSales ? ["Sales"] : [],
            NextSteps = new Dictionary<string, string> { ["QuoteViewed"] = "END" }
        });
        definition.AddStep(new WorkflowStepDefinition("END", WorkflowStepType.End));
        definition.AttachBusinessRoles(
        [
            new BusinessRoleDefinition { Name = "Sales", ResolutionType = "Assignment", Capabilities = ["event.publish"] },
            new BusinessRoleDefinition { Name = "Manager", ResolutionType = "Assignment", Capabilities = ["event.publish"] }
        ]);
        definition.Publish();

        var instance = new WorkflowInstance(tenantId, definition.Id, Guid.NewGuid(), 1, "Review");
        _context.WorkflowDefinitions.Add(definition);
        _context.WorkflowInstances.Add(instance);
        await _context.SaveChangesAsync();
        return (tenantId, instance);
    }
}
