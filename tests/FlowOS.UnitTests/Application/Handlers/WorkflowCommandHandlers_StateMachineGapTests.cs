using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Handlers;
using FlowOS.Core.Interfaces;
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
/// Documents a known architectural gap: <see cref="WorkflowCommandHandlers"/>.Handle(PublishEventCommand, ...)
/// advances a workflow purely by matching the incoming event against the current step's
/// NextSteps dictionary. It never loads a StateMachineDefinition for the underlying entity,
/// even though FlowOS.Workflows.Engine.WorkflowEngine.Advance fully supports state-machine
/// enforcement when given one (see WorkflowStateEnforcementTests).
///
/// Net effect: a State Machine that would deny a transition (proven directly against the
/// engine below) has zero effect on whether POST /api/events/publish actually succeeds,
/// because nothing in that request path ever consults it.
///
/// If this gap is closed (state machine wired into PublishEventCommand), the second test
/// below should start failing and must be updated to assert the transition is denied instead.
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
        // The underlying engine DOES support enforcement when given a
        // StateMachineDefinition and the entity's current state.
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
    public async Task Handle_PublishEventCommand_IgnoresStateMachine_EvenWhenOneExistsForTheEntity()
    {
        // Arrange: a workflow that will happily advance on EVT-CLOSE ...
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "GapTestWF", 1, "Start");
        definition.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command)
        {
            NextSteps = new Dictionary<string, string> { { "EVT-CLOSE", "End" } }
        });
        definition.AddStep(new WorkflowStepDefinition("End", WorkflowStepType.Command));
        definition.Publish();

        var instance = new WorkflowInstance(tenantId, definition.Id, Guid.Empty, 1, "Start");

        _context.WorkflowDefinitions.Add(definition);
        _context.WorkflowInstances.Add(instance);

        // ... even though a StateMachineDefinition for "Order" exists and would DENY
        // this exact event from a "Closed" state (proven in the sanity test above),
        // the handler never loads or references it.
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

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert: the workflow advances purely on the workflow-level NextSteps match.
        // The co-existing StateMachineDefinition is never consulted, so it cannot deny
        // the transition here even though a direct engine check (above) proves it would.
        Assert.True(result);
        var updated = await _context.WorkflowInstances.FindAsync(instance.Id);
        Assert.NotNull(updated);
        Assert.Equal("End", updated!.CurrentStepId);
    }
}
