using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Handlers;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Security.Interfaces;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using FlowOS.Workflows.Engine;
using FlowOS.StateMachines.Engine;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FlowOS.UnitTests.Application.Handlers;

public class WorkflowCommandHandlersTests : IDisposable
{
    private readonly FlowOSDbContext _context;
    private readonly Mock<IEventRegistry> _mockEventRegistry;
    private readonly Mock<ICurrentUser> _mockCurrentUser;
    private readonly Mock<ICapabilityService> _mockCapabilityService;
    private readonly WorkflowCommandHandlers _handler;

    public WorkflowCommandHandlersTests()
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
    public async Task Handle_StartWorkflowCommand_WithValidDefinitionId_ShouldCreateInstance()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "TestWF", 1, "Start");
        definition.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command));
        definition.Publish();
        _context.WorkflowDefinitions.Add(definition);
        await _context.SaveChangesAsync();

        var command = new StartWorkflowCommand(tenantId, definition.Id, null, null, Guid.Empty, null, null);

        // Act
        var instanceId = await _handler.Handle(command, CancellationToken.None);

        // Assert
        var instance = await _context.WorkflowInstances.FindAsync(instanceId);
        Assert.NotNull(instance);
        Assert.Equal(tenantId, instance.TenantId);
        Assert.Equal(definition.Id, instance.WorkflowDefinitionId);
        Assert.Equal("Start", instance.CurrentStepId);
    }

    [Fact]
    public async Task Handle_StartWorkflowCommand_WithMissingDefinition_ShouldThrow()
    {
        // Arrange
        var command = new StartWorkflowCommand(Guid.NewGuid(), null, "NonExistentWF", null, Guid.Empty, null, null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _handler.Handle(command, CancellationToken.None));
        Assert.Contains("No definition found for workflow 'NonExistentWF'", ex.Message);
    }

    [Fact]
    public async Task Handle_PublishEventCommand_WithValidEvent_ShouldAdvanceWorkflow()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var definition = new WorkflowDefinition(tenantId, "TestWF", 1, "Start");
        definition.AddStep(new WorkflowStepDefinition("Start", FlowOS.Workflows.Enums.WorkflowStepType.Command) 
        { 
            NextSteps = new Dictionary<string, string> { { "EVT-NEXT", "End" } } 
        });
        definition.AddStep(new WorkflowStepDefinition("End", FlowOS.Workflows.Enums.WorkflowStepType.End));
        definition.Publish();
        
        var instance = new WorkflowInstance(tenantId, definition.Id, Guid.Empty, 1, "Start");
        
        _context.WorkflowDefinitions.Add(definition);
        _context.WorkflowInstances.Add(instance);
        await _context.SaveChangesAsync();

        _mockCurrentUser.Setup(x => x.Roles).Returns(new List<string> { "User" });
        _mockCapabilityService
            .Setup(x => x.GetCapabilitiesAsync(tenantId, It.IsAny<List<string>>()))
            .ReturnsAsync(new HashSet<string> { "event.publish.EVT-NEXT" });
            
        _mockEventRegistry.Setup(x => x.ExistsAsync("EVT-NEXT", tenantId)).ReturnsAsync(true);

        var command = new PublishEventCommand(tenantId, instance.Id, "EVT-NEXT", null, null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result);
        var updatedInstance = await _context.WorkflowInstances.FindAsync(instance.Id);
        Assert.Equal("End", updatedInstance.CurrentStepId);
    }

    [Fact]
    public async Task Handle_PublishEventCommand_WithoutPermission_ShouldThrowPolicyViolation()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        
        _mockCurrentUser.Setup(x => x.Roles).Returns(new List<string> { "User" });
        _mockCapabilityService
            .Setup(x => x.GetCapabilitiesAsync(tenantId, It.IsAny<List<string>>()))
            .ReturnsAsync(new HashSet<string>()); // No permissions

        var command = new PublishEventCommand(tenantId, instanceId, "EVT-RESTRICTED", null, null);

        // Act & Assert
        await Assert.ThrowsAsync<FlowOS.Application.Common.Exceptions.PolicyViolationException>(
            () => _handler.Handle(command, CancellationToken.None));
    }
}
