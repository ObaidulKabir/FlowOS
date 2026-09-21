using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.Handlers;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Security.Interfaces;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using FlowOS.Workflows.Engine;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FlowOS.UnitTests.Application.Handlers;

/// <summary>
/// Class-backed live advance is fail-closed: a WorkflowClass without usable Law cannot
/// publish_event even if an unrelated StateMachineDefinition exists in the tenant.
/// Legacy Guid.Empty instances may still advance on the workflow graph.
/// </summary>
public class WorkflowCommandHandlers_StateMachineGapTests : IDisposable
{
    private readonly FlowOSDbContext _context;
    private readonly Mock<IEventRegistry> _mockEventRegistry;
    private readonly Mock<ICurrentUser> _mockCurrentUser;
    private readonly Mock<ICapabilityService> _mockCapabilityService;
    private readonly WorkflowCommandHandlers _handler;

    public WorkflowCommandHandlers_StateMachineGapTests()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FlowOSDbContext(options);
        _mockEventRegistry = new Mock<IEventRegistry>();
        _mockCurrentUser = new Mock<ICurrentUser>();
        _mockCapabilityService = new Mock<ICapabilityService>();

        _handler = new WorkflowCommandHandlers(
            new UnitOfWork(_context),
            _mockEventRegistry.Object,
            _mockCurrentUser.Object,
            _mockCapabilityService.Object,
            new WorkflowEngine(new StateMachineEngine())
        );
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public void Sanity_StateMachineEngine_DeniesTransition_WhenEntityIsInWrongState()
    {
        var smDef = new StateMachineDefinition(Guid.NewGuid(), "Order", "Created");
        smDef.AddState("Created");
        smDef.AddState("Closed");
        smDef.AddTransition(new StateTransition("Created", "Closed", "EVT-CLOSE"));

        var engine = new StateMachineEngine();
        var evt = new StandardEvent(Guid.NewGuid(), "EVT-CLOSE");

        var result = engine.ValidateTransition(smDef, "Closed", evt, new FlowOS.StateMachines.Models.ExecutionContext());

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public async Task Handle_PublishEventCommand_FailsClosed_WhenClassBackedInstanceHasNoUsableLaw()
    {
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "GapTestWF", 1, "Start");
        definition.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command)
        {
            NextSteps = new Dictionary<string, string> { { "EVT-CLOSE", "End" } }
        });
        definition.AddStep(new WorkflowStepDefinition("End", WorkflowStepType.Command));
        definition.Publish();

        var workflowClass = new WorkflowClass(tenantId, "GapTestClass", "1.0.0", new WorkflowClassBlueprint());
        definition.SetClassLineage(workflowClass.Id, Guid.NewGuid());
        var instance = new WorkflowInstance(tenantId, definition.Id, workflowClass.Id, 1, "Start");

        _context.WorkflowDefinitions.Add(definition);
        _context.WorkflowClasses.Add(workflowClass);
        _context.WorkflowInstances.Add(instance);

        var smDef = new StateMachineDefinition(tenantId, "Order", "Created");
        smDef.AddState("Created");
        smDef.AddState("Closed");
        smDef.AddTransition(new StateTransition("Created", "Closed", "EVT-CLOSE"));
        _context.StateMachineDefinitions.Add(smDef);

        await _context.SaveChangesAsync();

        _mockCurrentUser.Setup(x => x.Roles).Returns(new List<string> { "User" });
        _mockCapabilityService
            .Setup(x => x.GetCapabilitiesAsync(tenantId, It.IsAny<List<string>>()))
            .ReturnsAsync(new HashSet<string> { "event.publish.EVT-CLOSE" });
        _mockEventRegistry.Setup(x => x.ExistsAsync("EVT-CLOSE", tenantId)).ReturnsAsync(true);

        var command = new PublishEventCommand(tenantId, instance.Id, "EVT-CLOSE", null, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.Handle(command, CancellationToken.None));

        Assert.Contains("fail-closed", ex.Message, StringComparison.OrdinalIgnoreCase);
        var updated = await _context.WorkflowInstances.FindAsync(instance.Id);
        Assert.Equal("Start", updated!.CurrentStepId);
    }

    [Fact]
    public async Task Handle_PublishEventCommand_LegacyEmptyClassId_MayStillAdvanceOnWorkflowGraph()
    {
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "LegacyWF", 1, "Start");
        definition.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command)
        {
            NextSteps = new Dictionary<string, string> { { "EVT-CLOSE", "End" } }
        });
        definition.AddStep(new WorkflowStepDefinition("End", WorkflowStepType.Command));
        definition.Publish();

        var instance = new WorkflowInstance(tenantId, definition.Id, Guid.Empty, 1, "Start");
        _context.WorkflowDefinitions.Add(definition);
        _context.WorkflowInstances.Add(instance);
        await _context.SaveChangesAsync();

        _mockCurrentUser.Setup(x => x.Roles).Returns(new List<string> { "User" });
        _mockCapabilityService
            .Setup(x => x.GetCapabilitiesAsync(tenantId, It.IsAny<List<string>>()))
            .ReturnsAsync(new HashSet<string> { "event.publish.EVT-CLOSE" });
        _mockEventRegistry.Setup(x => x.ExistsAsync("EVT-CLOSE", tenantId)).ReturnsAsync(true);

        var result = await _handler.Handle(
            new PublishEventCommand(tenantId, instance.Id, "EVT-CLOSE", null, null),
            CancellationToken.None);

        Assert.True(result);
        var updated = await _context.WorkflowInstances.FindAsync(instance.Id);
        Assert.Equal("End", updated!.CurrentStepId);
    }
}
