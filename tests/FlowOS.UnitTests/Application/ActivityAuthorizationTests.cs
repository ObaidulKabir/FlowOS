using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Services;
using FlowOS.Security.Interfaces;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Moq;

namespace FlowOS.UnitTests.Application;

public class ActivityAuthorizationTests
{
    [Fact]
    public void HasGrant_AllowsCapabilityIntersection_EvenWhenRoleNameDiffers()
    {
        var caller = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "expense.approve" };
        Assert.True(ActivityAuthorization.HasGrant(caller, new[] { "expense.approve" }));
        Assert.False(ActivityAuthorization.HasGrant(caller, new[] { "expense.reject" }));
    }

    [Fact]
    public void HasGrant_AllowsEventPublishWildcard()
    {
        var caller = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "event.publish" };
        Assert.True(ActivityAuthorization.HasGrant(caller, new[] { "event.publish.EVT-APPROVE" }));
    }

    [Fact]
    public void ResolveRequiredCapabilities_PrefersPerEventCaps()
    {
        var step = new WorkflowStepDefinition("Review", WorkflowStepType.HumanTask)
        {
            RequiredCapabilities = new List<string> { "expense.approve" },
            EventRequiredCapabilities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["EVT-REJECT"] = new List<string> { "expense.reject" }
            }
        };

        Assert.Equal(new[] { "expense.reject" }, ActivityAuthorization.ResolveRequiredCapabilities(step, "EVT-REJECT"));
        Assert.Equal(new[] { "expense.approve" }, ActivityAuthorization.ResolveRequiredCapabilities(step, "EVT-APPROVE"));
    }

    [Fact]
    public async Task AuthorizeAsync_DirectorWithCapability_MayActWithoutInboxRole()
    {
        var capabilities = new Mock<ICapabilityService>();
        capabilities
            .Setup(service => service.GetCapabilitiesAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "expense.approve" });
        var auth = new ActivityAuthorizationService(capabilities.Object);

        await auth.AuthorizeAsync(
            Guid.NewGuid(),
            new[] { "Director" },
            new[] { "expense.approve" },
            failClosed: true,
            "ActivityAuthorization",
            "approve expense");
    }

    [Fact]
    public async Task AuthorizeAsync_EmptyRequired_FailsClosed()
    {
        var capabilities = new Mock<ICapabilityService>();
        capabilities
            .Setup(service => service.GetCapabilitiesAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new HashSet<string>());
        var auth = new ActivityAuthorizationService(capabilities.Object);

        await Assert.ThrowsAsync<PolicyViolationException>(() =>
            auth.AuthorizeAsync(
                Guid.NewGuid(),
                new[] { "Approver" },
                Array.Empty<string>(),
                failClosed: true,
                "ActivityAuthorization",
                "complete human task"));
    }

    [Fact]
    public async Task AuthorizeAsync_AdminBypasses()
    {
        var capabilities = new Mock<ICapabilityService>();
        var auth = new ActivityAuthorizationService(capabilities.Object);

        Assert.True(await auth.IsAuthorizedAsync(
            Guid.NewGuid(),
            new[] { "Admin" },
            new[] { "expense.approve" },
            failClosed: true));
        capabilities.Verify(
            service => service.GetCapabilitiesAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<string>>()),
            Times.Never);
    }
}
