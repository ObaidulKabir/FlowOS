using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using FlowOS.Security.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.MCP.UnitTests.Tools;

public class TenantTeamMcpToolsTests
{
    private readonly Mock<ITeamRepository> _mockRepo;
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly Mock<ICurrentUser> _mockUser;
    private readonly Mock<ICapabilityService> _mockCap;
    private readonly McpAuthorizationErrorMapper _errorMapper;
    private readonly TenantTeamMcpTools _tools;
    private readonly Guid _tenantId = Guid.NewGuid();

    public TenantTeamMcpToolsTests()
    {
        _mockRepo = new Mock<ITeamRepository>();
        _mockUow = new Mock<IUnitOfWork>();
        _mockUser = new Mock<ICurrentUser>();
        _mockCap = new Mock<ICapabilityService>();

        _mockUser.Setup(u => u.Id).Returns(Guid.NewGuid().ToString());
        _mockUser.Setup(u => u.Roles).Returns(new List<string> { "Admin" });
        _mockUser.Setup(u => u.IsApiKey).Returns(false);

        _errorMapper = new McpAuthorizationErrorMapper(_mockUser.Object, _mockCap.Object);
        _tools = new TenantTeamMcpTools(_mockRepo.Object, _mockUow.Object, _mockUser.Object, _mockCap.Object, _errorMapper, NullLogger<TenantTeamMcpTools>.Instance);
    }

    [Fact]
    public async Task ListTenantTeams_WithPermission_ReturnsTeams()
    {
        _mockCap.Setup(c => c.GetCapabilitiesAsync(_tenantId, It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync(new HashSet<string> { "iam.read" });

        var team = new Team(_tenantId, "Test Team", "Desc", new List<string> { "cap1" });
        team.SetHierarchy(new List<TeamHierarchyLevel> { new TeamHierarchyLevel("Mbr", 0) });
        
        _mockRepo.Setup(r => r.ListByCapabilitiesAsync(_tenantId, It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Team> { team });

        var args = JObject.Parse($"{{\"tenantId\":\"{_tenantId}\"}}");
        var result = await _tools.ListTenantTeams(args);

        Assert.False(result.IsError);
        Assert.NotNull(result.Content);
    }

    [Fact]
    public async Task ListTenantTeams_WithoutPermission_ReturnsAuthzError()
    {
        _mockCap.Setup(c => c.GetCapabilitiesAsync(_tenantId, It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync(new HashSet<string>()); // No iam.read

        var args = JObject.Parse($"{{\"tenantId\":\"{_tenantId}\"}}");
        var result = await _tools.ListTenantTeams(args);

        Assert.True(result.IsError);
        Assert.Contains("MCP-AUTHZ-001", result.Content[0].Text);
    }

    [Fact]
    public async Task CreateTenantTeam_WithValidDataAndPermission_CreatesTeam()
    {
        _mockCap.Setup(c => c.GetCapabilitiesAsync(_tenantId, It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync(new HashSet<string> { "iam.manage" });

        var args = JObject.Parse($"{{\"tenantId\":\"{_tenantId}\",\"name\":\"Devs\",\"confirmHumanApproval\":true}}");
        var result = await _tools.CreateTenantTeam(args);

        Assert.False(result.IsError);
        _mockRepo.Verify(r => r.Add(It.Is<Team>(t => t.Name == "Devs")), Times.Once);
        _mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddTeamMember_WithValidDataAndPermission_AddsMember()
    {
        _mockCap.Setup(c => c.GetCapabilitiesAsync(_tenantId, It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync(new HashSet<string> { "iam.manage" });

        var teamId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var team = new Team(_tenantId, "Test Team", "Desc");
        team.SetHierarchy(new List<TeamHierarchyLevel> { new TeamHierarchyLevel("Manager", 1) });
        
        _mockRepo.Setup(r => r.GetByIdAsync(_tenantId, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(team);

        var args = JObject.Parse($"{{\"tenantId\":\"{_tenantId}\",\"teamId\":\"{teamId}\",\"userId\":\"{userId}\",\"levelName\":\"Manager\",\"confirmHumanApproval\":true}}");
        var result = await _tools.AddTeamMember(args);

        Assert.False(result.IsError);
        Assert.Single(team.Members);
        _mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
