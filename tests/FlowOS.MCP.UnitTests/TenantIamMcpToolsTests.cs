using FlowOS.Application.Commands.Security;
using FlowOS.Core.Interfaces;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using FlowOS.Security.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

public class TenantIamMcpToolsTests
{
    [Fact]
    public async Task DiagnoseCallerPermissions_ReportsScopeRestrictedEffectiveGrant()
    {
        var tenantId = Guid.NewGuid();
        var (tool, capabilities, _) = CreateTool(
            tenantId,
            scopes: new[] { "workflow:read" });
        capabilities
            .Setup(service => service.GetCapabilitiesAsync(tenantId, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "workflow.read",
                "workflow.start"
            });
        capabilities
            .Setup(service => service.GetEffectiveCapabilitiesAsync(
                tenantId,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                true))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "workflow.read"
            });

        McpRequestContext.TenantId = tenantId;
        try
        {
            var result = await tool.DiagnoseCallerPermissions(JObject.FromObject(new
            {
                tenantId,
                requiredCapability = "workflow.start"
            }));
            var payload = JObject.Parse(result.Content.Single().Text!);

            Assert.False(result.IsError);
            Assert.False(payload["data"]!["authorized"]!.Value<bool>());
            Assert.Equal("workflow:read", payload["data"]!["scopes"]![0]!.Value<string>());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task CreateTenantRole_WithoutExplicitApproval_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        var (tool, _, mediator) = CreateTool(tenantId, scopes: new[] { "*" });

        McpRequestContext.TenantId = tenantId;
        try
        {
            var result = await tool.CreateTenantRole(JObject.FromObject(new
            {
                tenantId,
                roleName = "SalesFlowOperator"
            }));
            var payload = JObject.Parse(result.Content.Single().Text!);

            Assert.True(result.IsError);
            Assert.Equal("MCP-APPROVAL-REQUIRED", payload["errorCode"]!.Value<string>());
            mediator.Verify(
                item => item.Send(It.IsAny<CreateRoleCommand>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task GrantRoleCapability_WithIamManageAndApproval_UsesGovernedCommand()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var (tool, capabilities, mediator) = CreateTool(tenantId, scopes: new[] { "*" });
        capabilities
            .Setup(service => service.GetEffectiveCapabilitiesAsync(
                tenantId,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                true))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "iam.manage" });
        mediator
            .Setup(item => item.Send(It.IsAny<AddCapabilityToRoleCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        McpRequestContext.TenantId = tenantId;
        try
        {
            var result = await tool.GrantRoleCapability(JObject.FromObject(new
            {
                tenantId,
                roleId,
                capabilityCode = "workflow.start",
                confirmHumanApproval = true
            }));
            var payload = JObject.Parse(result.Content.Single().Text!);

            Assert.False(result.IsError);
            Assert.Equal("granted", payload["data"]!["operation"]!.Value<string>());
            mediator.Verify(
                item => item.Send(
                    It.Is<AddCapabilityToRoleCommand>(command =>
                        command.TenantId == tenantId &&
                        command.RoleId == roleId &&
                        command.CapabilityCode == "workflow.start"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    private static (
        TenantIamMcpTools Tool,
        Mock<ICapabilityService> Capabilities,
        Mock<IMediator> Mediator) CreateTool(
        Guid tenantId,
        IReadOnlyCollection<string> scopes)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.Id).Returns("mcp-agent");
        currentUser.SetupGet(user => user.TenantId).Returns(tenantId);
        currentUser.SetupGet(user => user.Roles).Returns(new List<string> { "Admin" });
        currentUser.SetupGet(user => user.Scopes).Returns(scopes);
        currentUser.SetupGet(user => user.IsApiKey).Returns(true);

        var capabilities = new Mock<ICapabilityService>();
        var mediator = new Mock<IMediator>();
        var mapper = new McpAuthorizationErrorMapper(currentUser.Object, capabilities.Object);
        var tool = new TenantIamMcpTools(
            mediator.Object,
            currentUser.Object,
            capabilities.Object,
            mapper,
            NullLogger<TenantIamMcpTools>.Instance);

        return (tool, capabilities, mediator);
    }
}
