using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.Services;
using FlowOS.Domain.Entities;
using FlowOS.Workflows.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FlowOS.UnitTests.Application.Services;

public class TeamEscalationServiceTests
{
    private readonly Mock<ITeamRepository> _mockRepo;
    private readonly TeamEscalationService _service;

    public TeamEscalationServiceTests()
    {
        _mockRepo = new Mock<ITeamRepository>();
        _service = new TeamEscalationService(_mockRepo.Object, NullLogger<TeamEscalationService>.Instance);
    }

    [Fact]
    public async Task TryEscalateTeamRolesAsync_NoRoleAssignments_ReturnsFalse()
    {
        var definition = new WorkflowDefinition(Guid.NewGuid(), "test", 1, "start");
        var instance = new WorkflowInstance(definition.TenantId, definition.Id, Guid.NewGuid(), 1, "step1");

        var result = await _service.TryEscalateTeamRolesAsync(definition, instance);

        Assert.False(result);
    }

    [Fact]
    public async Task TryEscalateTeamRolesAsync_TeamAssigned_EscalatesToNextLevel()
    {
        var tenantId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        
        var stepDef = new WorkflowStepDefinition
        {
            StepId = "step1",
            AllowedRoles = new List<string> { "Approver" }
        };
        var definition = new WorkflowDefinition(tenantId, "test", 1, "step1");
        definition.AddStep(stepDef);
        
        var instance = new WorkflowInstance(tenantId, definition.Id, Guid.NewGuid(), 1, "step1");
        instance.SetCurrentState("Waiting");
        instance.AssignRole("Approver", $"team:{teamId}:0");

        var team = new Team(tenantId, "Team", "Desc");
        team.SetHierarchy(new List<TeamHierarchyLevel>
        {
            new TeamHierarchyLevel("Level 0", 0),
            new TeamHierarchyLevel("Level 1", 1)
        });

        _mockRepo.Setup(r => r.GetByIdAsync(tenantId, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(team);

        var result = await _service.TryEscalateTeamRolesAsync(definition, instance);

        Assert.True(result);
        Assert.Equal($"team:{teamId}:1", instance.RoleAssignments["Approver"]);
    }

    [Fact]
    public async Task TryEscalateTeamRolesAsync_MaxLevelReached_ReturnsFalse()
    {
        var tenantId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        
        var stepDef = new WorkflowStepDefinition
        {
            StepId = "step1",
            AllowedRoles = new List<string> { "Approver" }
        };
        var definition = new WorkflowDefinition(tenantId, "test", 1, "step1");
        definition.AddStep(stepDef);
        
        var instance = new WorkflowInstance(tenantId, definition.Id, Guid.NewGuid(), 1, "step1");
        instance.SetCurrentState("Waiting");
        instance.AssignRole("Approver", $"team:{teamId}:1"); // Already at max level

        var team = new Team(tenantId, "Team", "Desc");
        team.SetHierarchy(new List<TeamHierarchyLevel>
        {
            new TeamHierarchyLevel("Level 0", 0),
            new TeamHierarchyLevel("Level 1", 1)
        });

        _mockRepo.Setup(r => r.GetByIdAsync(tenantId, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(team);

        var result = await _service.TryEscalateTeamRolesAsync(definition, instance);

        Assert.False(result);
        Assert.Equal($"team:{teamId}:1", instance.RoleAssignments["Approver"]); // Unchanged
    }
}
