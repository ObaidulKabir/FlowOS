using System;
using System.Collections.Generic;
using FlowOS.Domain.Entities;
using Xunit;

namespace FlowOS.UnitTests.Domain.Entities;

public class TeamTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesTeam()
    {
        var tenantId = Guid.NewGuid();
        var team = new Team(tenantId, "Artwork Team", "Handles artwork", new List<string> { "artwork.review" });

        Assert.Equal(tenantId, team.TenantId);
        Assert.Equal("Artwork Team", team.Name);
        Assert.Equal("Handles artwork", team.Description);
        Assert.Contains("artwork.review", team.Capabilities);
    }

    [Fact]
    public void SetHierarchy_ValidLevels_SetsOrderedHierarchy()
    {
        var team = new Team(Guid.NewGuid(), "Team", "Desc");
        
        var levels = new List<TeamHierarchyLevel>
        {
            new TeamHierarchyLevel("Admin", 2),
            new TeamHierarchyLevel("Member", 0),
            new TeamHierarchyLevel("Manager", 1)
        };

        team.SetHierarchy(levels);

        Assert.Equal(3, team.Hierarchy.Count);
        Assert.Equal("Member", team.Hierarchy[0].Name);
        Assert.Equal("Manager", team.Hierarchy[1].Name);
        Assert.Equal("Admin", team.Hierarchy[2].Name);
    }

    [Fact]
    public void AddMember_ValidUserAndLevel_AddsMember()
    {
        var team = new Team(Guid.NewGuid(), "Team", "Desc");
        team.SetHierarchy(new List<TeamHierarchyLevel> { new TeamHierarchyLevel("Member", 0) });

        var userId = Guid.NewGuid();
        var member = team.AddMember(userId, "Member");

        Assert.Equal(userId, member.TenantUserId);
        Assert.Equal(0, member.HierarchyOrder);
        Assert.Single(team.Members);
    }

    [Fact]
    public void GetNextHigherLevel_ReturnsCorrectLevel()
    {
        var team = new Team(Guid.NewGuid(), "Team", "Desc");
        team.SetHierarchy(new List<TeamHierarchyLevel>
        {
            new TeamHierarchyLevel("Member", 0),
            new TeamHierarchyLevel("Manager", 1),
            new TeamHierarchyLevel("Admin", 2)
        });

        var next1 = team.GetNextHigherLevel(0);
        Assert.NotNull(next1);
        Assert.Equal("Manager", next1.Name);

        var next2 = team.GetNextHigherLevel(1);
        Assert.NotNull(next2);
        Assert.Equal("Admin", next2.Name);

        var next3 = team.GetNextHigherLevel(2);
        Assert.Null(next3);
    }
}
