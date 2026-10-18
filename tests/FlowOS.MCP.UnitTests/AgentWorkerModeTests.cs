using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Services;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using FlowOS.Workflows.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

public sealed class AgentWorkerModeTests
{
    [Fact]
    public void DedicatedHost_OnlyStartsTheAgentProcessor()
    {
        using var host = Program.BuildWorkerHost([
            "--environment", "Development",
            "--ConnectionStrings:DefaultConnection",
            "Host=localhost;Database=worker_di_only;Username=unused;Password=unused"
        ]);
        var services = host.Services.GetServices<IHostedService>().ToList();

        Assert.Single(services);
        Assert.IsType<AgentTaskProcessorService>(services[0]);

        using var scope = host.Services.CreateScope();
        Assert.IsType<AgentWorkerCurrentUser>(
            scope.ServiceProvider.GetRequiredService<ICurrentUser>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<TenantTeamMcpTools>());
    }

    [Fact]
    public async Task RunAgentTask_WhenInlineExecutionDisabled_WaitsWithoutClaiming()
    {
        var tenantId = Guid.NewGuid();
        var instance = new WorkflowInstance(
            tenantId,
            Guid.NewGuid(),
            Guid.Empty,
            1,
            "AgentReview");
        var instances = new Mock<IWorkflowInstanceRepository>();
        instances.Setup(repository => repository.GetByIdAsNoTrackingAsync(
                instance.Id, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(work => work.WorkflowInstances).Returns(instances.Object);

        var coordinator = new Mock<IAgentTaskCoordinator>();
        var jobId = Guid.NewGuid();
        coordinator.Setup(work => work.EnqueueAndWaitAsync(
                It.IsAny<AgentTaskEnqueueRequest>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTaskSynchronousResult(
                jobId,
                AgentTaskJobStatus.Pending,
                null,
                TimedOut: true));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FlowOS:Agents:Worker:ExecuteInMcpRequests"] = "false"
            })
            .Build();
        var tools = new AgentTools(
            unitOfWork.Object,
            coordinator.Object,
            configuration: configuration);

        try
        {
            var response = await tools.RunAgentTask(JObject.FromObject(new
            {
                tenantId,
                workflowInstanceId = instance.Id
            }));

            Assert.False(response.IsError);
            coordinator.Verify(work => work.EnqueueAndWaitAsync(
                It.Is<AgentTaskEnqueueRequest>(request =>
                    request.TenantId == tenantId &&
                    request.WorkflowInstanceId == instance.Id &&
                    request.AllowAutoCommit),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()), Times.Once);
            coordinator.Verify(work => work.EnqueueAndExecuteAsync(
                It.IsAny<AgentTaskEnqueueRequest>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task JobStatus_HidesOtherTenantsJobs()
    {
        var tenantId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var queue = new Mock<IAgentTaskQueue>();
        queue.Setup(service => service.GetAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTaskJobSnapshot(
                jobId, Guid.NewGuid(), Guid.NewGuid(), "Review", "Agent",
                null, true, true, AgentTaskSource.WorkflowEntry,
                AgentTaskJobStatus.Completed, 1, 5,
                DateTime.UtcNow, DateTime.UtcNow, null, null,
                DateTime.UtcNow, null, null));
        var tools = new AgentTools(
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IAgentTaskCoordinator>(),
            agentTaskQueue: queue.Object);

        try
        {
            var result = await tools.GetAgentTaskJob(JObject.FromObject(new { jobId, tenantId }));
            Assert.True(result.IsError);
            Assert.Contains("not found", result.Content[0].Text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }
}
