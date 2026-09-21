using FlowOS.Api.Controllers;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace FlowOS.UnitTests.Integration;

public sealed class AgentsControllerObservabilityTests
{
    [Fact]
    public void Controller_RequiresAuthentication()
    {
        var authorize = typeof(AgentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);

        Assert.NotEmpty(authorize);
    }

    [Fact]
    public async Task InstanceHistory_UsesAuthenticatedTenantWindowAndCancellation()
    {
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var from = new DateTimeOffset(
            2026,
            9,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        var to = from.AddDays(7);
        var expected = new AgentExecutionHistoryDto(
            from.UtcDateTime,
            to.UtcDateTime,
            25,
            false,
            Array.Empty<AgentExecutionDto>());
        var observability = new Mock<IAgentObservabilityQueryService>();
        AgentExecutionHistoryRequest? captured = null;
        CancellationToken capturedToken = default;
        observability
            .Setup(service => service.GetExecutionHistoryAsync(
                It.IsAny<AgentExecutionHistoryRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<AgentExecutionHistoryRequest, CancellationToken>(
                (request, cancellationToken) =>
                {
                    captured = request;
                    capturedToken = cancellationToken;
                })
            .ReturnsAsync(expected);
        using var cancellation = new CancellationTokenSource();

        var controller = Controller(tenantId, observability.Object);
        var response = await controller.GetExecutionHistory(
            instanceId,
            from,
            to,
            "Succeeded",
            25,
            cancellation.Token);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(expected, ok.Value);
        Assert.NotNull(captured);
        Assert.Equal(tenantId, captured!.TenantId);
        Assert.Equal(instanceId, captured.WorkflowInstanceId);
        Assert.Equal(from.UtcDateTime, captured.FromUtc);
        Assert.Equal(to.UtcDateTime, captured.ToUtc);
        Assert.Equal(25, captured.Limit);
        Assert.Equal("Succeeded", captured.Status?.ToString());
        Assert.Equal(cancellation.Token, capturedToken);
    }

    [Fact]
    public async Task Metrics_RejectNonUtcAndOverlongWindowsBeforeQuery()
    {
        var observability = new Mock<IAgentObservabilityQueryService>();
        var controller = Controller(Guid.NewGuid(), observability.Object);
        var from = new DateTimeOffset(
            2026,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.FromHours(6));
        var to = from.AddDays(91);

        var response = await controller.GetEvaluationMetrics(
            from,
            to,
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response.Result);
        observability.Verify(
            service => service.GetEvaluationMetricsAsync(
                It.IsAny<AgentEvaluationMetricsRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static AgentsController Controller(
        Guid tenantId,
        IAgentObservabilityQueryService observability)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.TenantId).Returns(tenantId);
        currentUser.SetupGet(user => user.Roles).Returns(new List<string>());
        return new AgentsController(
            Mock.Of<IMediator>(),
            currentUser.Object,
            observability,
            TimeProvider.System);
    }
}
