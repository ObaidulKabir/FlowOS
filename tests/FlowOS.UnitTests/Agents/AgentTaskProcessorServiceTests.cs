using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.API.Services;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FlowOS.UnitTests.Agents;

public class AgentTaskProcessorServiceTests
{
    [Fact]
    public async Task Service_PollsQueue_AndProcessesClaims()
    {
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var queueMock = new Mock<IAgentTaskQueue>();
        var coordinatorMock = new Mock<IAgentTaskCoordinator>();
        var cleanupMock = new Mock<IAgentPersistenceCleanupService>();
        var loggerMock = new Mock<ILogger<AgentTaskProcessorService>>();
        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UtcNow);

        scopeFactoryMock.Setup(s => s.CreateScope()).Returns(scopeMock.Object);
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        serviceProviderMock.Setup(p => p.GetService(typeof(IAgentTaskQueue))).Returns(queueMock.Object);
        serviceProviderMock.Setup(p => p.GetService(typeof(IAgentTaskCoordinator))).Returns(coordinatorMock.Object);
        serviceProviderMock.Setup(p => p.GetService(typeof(IAgentPersistenceCleanupService))).Returns(cleanupMock.Object);

        var claims = new List<AgentTaskClaim>
        {
            new AgentTaskClaim(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "step1", "agent1", "objective", true, true,
                AgentTaskSource.WorkflowEntry, 1, 3, DateTime.UtcNow, DateTime.UtcNow,
                DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5), "claimant")
        };

        queueMock.Setup(q => q.ClaimBatchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claims);

        var options = Options.Create(new AgentTaskProcessorOptions
        {
            PollIntervalMilliseconds = 50,
            BatchSize = 10,
            MaxParallelism = 1
        });

        var service = new AgentTaskProcessorService(
            scopeFactoryMock.Object,
            options,
            loggerMock.Object,
            timeProviderMock.Object);

        using var cts = new CancellationTokenSource();
        
        var executeTask = service.StartAsync(cts.Token);
        
        await Task.Delay(150, CancellationToken.None);
        cts.Cancel();
        
        try { await service.StopAsync(CancellationToken.None); } catch (TaskCanceledException) { }

        queueMock.Verify(q => q.ClaimBatchAsync(It.IsAny<string>(), 10, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        coordinatorMock.Verify(c => c.ProcessClaimAsync(claims[0], It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
    
    [Fact]
    public async Task Service_TriggersCleanup_OnInterval()
    {
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var queueMock = new Mock<IAgentTaskQueue>();
        var coordinatorMock = new Mock<IAgentTaskCoordinator>();
        var cleanupMock = new Mock<IAgentPersistenceCleanupService>();
        var loggerMock = new Mock<ILogger<AgentTaskProcessorService>>();
        
        var timeProviderMock = new Mock<TimeProvider>();
        var now = DateTimeOffset.UtcNow;
        timeProviderMock.SetupSequence(t => t.GetUtcNow())
            .Returns(now)
            .Returns(now.AddMinutes(2))
            .Returns(now.AddMinutes(2));

        scopeFactoryMock.Setup(s => s.CreateScope()).Returns(scopeMock.Object);
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        serviceProviderMock.Setup(p => p.GetService(typeof(IAgentTaskQueue))).Returns(queueMock.Object);
        serviceProviderMock.Setup(p => p.GetService(typeof(IAgentTaskCoordinator))).Returns(coordinatorMock.Object);
        serviceProviderMock.Setup(p => p.GetService(typeof(IAgentPersistenceCleanupService))).Returns(cleanupMock.Object);

        queueMock.Setup(q => q.ClaimBatchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AgentTaskClaim>());
            
        cleanupMock.Setup(c => c.CleanupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentPersistenceCleanupResult(1, 0, 0, 0));

        var options = Options.Create(new AgentTaskProcessorOptions
        {
            PollIntervalMilliseconds = 50,
            CleanupIntervalMinutes = 1
        });

        var service = new AgentTaskProcessorService(
            scopeFactoryMock.Object,
            options,
            loggerMock.Object,
            timeProviderMock.Object);

        using var cts = new CancellationTokenSource();
        
        var executeTask = service.StartAsync(cts.Token);
        
        await Task.Delay(150, CancellationToken.None);
        cts.Cancel();
        
        try { await service.StopAsync(CancellationToken.None); } catch (TaskCanceledException) { }

        cleanupMock.Verify(c => c.CleanupAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
