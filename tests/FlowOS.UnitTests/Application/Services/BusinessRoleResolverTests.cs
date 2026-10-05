using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.Services;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Workflows.Domain;
using Moq;
using Xunit;

namespace FlowOS.UnitTests.Application.Services;

public class BusinessRoleResolverTests
{
    private readonly Mock<ITeamRepository> _mockRepo = new();
    private readonly Mock<ICurrentUser> _mockUser = new();

    [Fact]
    public async Task ResolveCallerRolesAsync_AssignmentType_MatchesCallerRef()
    {
        var tenantId = Guid.NewGuid();
        var def = new WorkflowDefinition(tenantId, "test", 1, "start");
        def.AttachBusinessRoles(new[] { new BusinessRoleDefinition { Name = "Approver", ResolutionType = "Assignment" } });
        
        var instance = new WorkflowInstance(tenantId, def.Id, Guid.NewGuid(), 1, "start");
        instance.AssignRole("Approver", "user-123");

        var resolver = new BusinessRoleResolver(_mockRepo.Object, _mockUser.Object);

        var roles = await resolver.ResolveCallerRolesAsync(def, instance, null, "user-123");
        Assert.Contains("Approver", roles);

        var rolesOther = await resolver.ResolveCallerRolesAsync(def, instance, null, "user-456");
        Assert.Empty(rolesOther);
    }

    [Fact]
    public async Task ResolveCallerRolesAsync_AssignmentType_WithTeamAssignee_ChecksTeamMembershipAndLevel()
    {
        var tenantId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var def = new WorkflowDefinition(tenantId, "test", 1, "start");
        def.AttachBusinessRoles(new[] { new BusinessRoleDefinition { Name = "Approver", ResolutionType = "Assignment" } });
        
        var instance = new WorkflowInstance(tenantId, def.Id, Guid.NewGuid(), 1, "start");
        instance.AssignRole("Approver", $"team:{teamId}:1"); // Requires level 1 or higher

        var team = new Team(tenantId, "Artwork", "Desc");
        team.SetHierarchy(new List<TeamHierarchyLevel>
        {
            new TeamHierarchyLevel("Member", 0),
            new TeamHierarchyLevel("Manager", 1)
        });
        team.AddMember(userId, "Manager"); // Level 1
        team.AddMember(otherUserId, "Member"); // Level 0

        _mockRepo.Setup(r => r.GetByIdAsync(tenantId, teamId, default))
            .ReturnsAsync(team);

        var resolver = new BusinessRoleResolver(_mockRepo.Object, _mockUser.Object);

        // User at level 1 (Authorized)
        var rolesUser = await resolver.ResolveCallerRolesAsync(def, instance, null, userId.ToString());
        Assert.Contains("Approver", rolesUser);

        // User at level 0 (Not authorized)
        var rolesOther = await resolver.ResolveCallerRolesAsync(def, instance, null, otherUserId.ToString());
        Assert.Empty(rolesOther);

        // Unknown user (Not authorized)
        var rolesUnknown = await resolver.ResolveCallerRolesAsync(def, instance, null, Guid.NewGuid().ToString());
        Assert.Empty(rolesUnknown);
    }

    [Fact]
    public async Task ResolveCallerRolesAsync_TeamResolutionType_ChecksCapabilities()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var def = new WorkflowDefinition(tenantId, "test", 1, "start");
        def.AttachBusinessRoles(new[] { new BusinessRoleDefinition { Name = "SystemApprover", ResolutionType = "Team", Capabilities = new List<string> { "cap1" } } });
        
        var instance = new WorkflowInstance(tenantId, def.Id, Guid.NewGuid(), 1, "start");

        _mockRepo.Setup(r => r.IsUserAuthorizedForCapabilitiesAsync(tenantId, userId, It.Is<IReadOnlyList<string>>(c => c.Contains("cap1")), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockRepo.Setup(r => r.IsUserAuthorizedForCapabilitiesAsync(tenantId, It.Is<Guid>(g => g != userId), It.IsAny<IReadOnlyList<string>>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var resolver = new BusinessRoleResolver(_mockRepo.Object, _mockUser.Object);

        var rolesUser = await resolver.ResolveCallerRolesAsync(def, instance, null, userId.ToString());
        Assert.Contains("SystemApprover", rolesUser);

        var rolesOther = await resolver.ResolveCallerRolesAsync(def, instance, null, Guid.NewGuid().ToString());
        Assert.Empty(rolesOther);
    }
}
