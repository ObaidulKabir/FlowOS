using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Entities.ExternalAI;
using FlowOS.Infrastructure.Persistence;
using FlowOS.MCP.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace FlowOS.UnitTests.Agents;

public sealed class ExternalAIAgentAutoPilotServiceTests
{
    [Fact]
    public async Task AutoPilot_Processes_Change_And_Acks_Success()
    {
        var tenant = CreateAutoPilotTenant();
        var change = ExternalAgentChangeRecord.Create(Guid.NewGuid(), tenant.TenantId, Guid.NewGuid(), "InvoiceOverdue", "{\"invoiceId\":\"INV-1\"}", DateTime.UtcNow);
        change.MarkLeased($"autopilot:{tenant.TenantId:N}", TimeSpan.FromMinutes(2));

        var changeStore = new Mock<IExternalAgentChangeStore>();
        changeStore.Setup(x => x.LeaseNextAsync(tenant.TenantId, $"autopilot:{tenant.TenantId:N}", It.IsAny<TimeSpan>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([change]);

        var planner = new Mock<IExternalAIAgentPlanner>();
        planner.Setup(x => x.PlanAsync(change.Id, tenant.TenantId, "ops", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalAIAgentPlanResult(
                Guid.NewGuid(),
                change.Id,
                tenant.TenantId,
                "ops",
                Array.Empty<string>(),
                [new ExternalAIAgentPlannedStep("step-1", 0, "get_external_agent_change", "{}", null, Array.Empty<string>())],
                "[]"));

        var executor = new Mock<IExternalAIAgentExecutor>();
        executor.Setup(x => x.ExecutePlanAsync(It.IsAny<Guid>(), tenant.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalAIAgentExecutionResult(
                Guid.NewGuid(),
                tenant.TenantId,
                true,
                [new ExternalAIAgentExecutedStep("step-1", 0, "get_external_agent_change", "Succeeded", DateTime.UtcNow, DateTime.UtcNow, null, "{\"ok\":true}")]));

        await using var provider = BuildProvider(tenant, changeStore.Object, planner.Object, executor.Object);
        var logger = new CapturingLogger();
        var service = new ExternalAIAgentAutoPilotService(provider, logger, TimeSpan.FromMilliseconds(10), TimeSpan.FromMinutes(2));

        await service.ProcessOnceAsync();

        planner.Verify(x => x.PlanAsync(change.Id, tenant.TenantId, "ops", null, It.IsAny<CancellationToken>()), Times.Once);
        executor.Verify(x => x.ExecutePlanAsync(It.IsAny<Guid>(), tenant.TenantId, It.IsAny<CancellationToken>()), Times.Once);
        changeStore.Verify(x => x.AckAsync(change.Id, true, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AutoPilot_Acks_Failure_With_Summary_When_Execution_Fails()
    {
        var tenant = CreateAutoPilotTenant();
        var change = ExternalAgentChangeRecord.Create(Guid.NewGuid(), tenant.TenantId, Guid.NewGuid(), "InvoiceOverdue", "{\"invoiceId\":\"INV-2\"}", DateTime.UtcNow);
        change.MarkLeased($"autopilot:{tenant.TenantId:N}", TimeSpan.FromMinutes(2));

        var changeStore = new Mock<IExternalAgentChangeStore>();
        changeStore.Setup(x => x.LeaseNextAsync(tenant.TenantId, $"autopilot:{tenant.TenantId:N}", It.IsAny<TimeSpan>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([change]);

        var planner = new Mock<IExternalAIAgentPlanner>();
        planner.Setup(x => x.PlanAsync(change.Id, tenant.TenantId, "ops", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalAIAgentPlanResult(
                Guid.NewGuid(),
                change.Id,
                tenant.TenantId,
                "ops",
                Array.Empty<string>(),
                [new ExternalAIAgentPlannedStep("step-1", 0, "publish_event", "{}", null, Array.Empty<string>())],
                "[]"));

        var executor = new Mock<IExternalAIAgentExecutor>();
        executor.Setup(x => x.ExecutePlanAsync(It.IsAny<Guid>(), tenant.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalAIAgentExecutionResult(
                Guid.NewGuid(),
                tenant.TenantId,
                false,
                [
                    new ExternalAIAgentExecutedStep("step-1", 0, "publish_event", "Succeeded", DateTime.UtcNow, DateTime.UtcNow, null, "{\"ok\":true}"),
                    new ExternalAIAgentExecutedStep("step-2", 1, "complete_task", "Failed", DateTime.UtcNow, DateTime.UtcNow, "boom", null)
                ]));

        await using var provider = BuildProvider(tenant, changeStore.Object, planner.Object, executor.Object);
        var service = new ExternalAIAgentAutoPilotService(provider, new CapturingLogger());

        await service.ProcessOnceAsync();

        changeStore.Verify(
            x => x.AckAsync(
                change.Id,
                false,
                It.Is<string>(message => message.Contains("step-2:Failed", StringComparison.Ordinal) && message.Contains("boom", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AutoPilot_Logs_Tenant_Change_And_Plan_Scopes()
    {
        var tenant = CreateAutoPilotTenant();
        var change = ExternalAgentChangeRecord.Create(Guid.NewGuid(), tenant.TenantId, Guid.NewGuid(), "InvoiceOverdue", "{\"invoiceId\":\"INV-3\"}", DateTime.UtcNow);
        change.MarkLeased($"autopilot:{tenant.TenantId:N}", TimeSpan.FromMinutes(2));
        var planId = Guid.NewGuid();

        var changeStore = new Mock<IExternalAgentChangeStore>();
        changeStore.Setup(x => x.LeaseNextAsync(tenant.TenantId, $"autopilot:{tenant.TenantId:N}", It.IsAny<TimeSpan>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([change]);

        var planner = new Mock<IExternalAIAgentPlanner>();
        planner.Setup(x => x.PlanAsync(change.Id, tenant.TenantId, "ops", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalAIAgentPlanResult(
                planId,
                change.Id,
                tenant.TenantId,
                "ops",
                Array.Empty<string>(),
                [new ExternalAIAgentPlannedStep("step-1", 0, "get_external_agent_change", "{}", null, Array.Empty<string>())],
                "[]"));

        var executor = new Mock<IExternalAIAgentExecutor>();
        executor.Setup(x => x.ExecutePlanAsync(planId, tenant.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalAIAgentExecutionResult(
                planId,
                tenant.TenantId,
                true,
                [new ExternalAIAgentExecutedStep("step-1", 0, "get_external_agent_change", "Succeeded", DateTime.UtcNow, DateTime.UtcNow, null, "{\"ok\":true}")]));

        await using var provider = BuildProvider(tenant, changeStore.Object, planner.Object, executor.Object);
        var logger = new CapturingLogger();
        var service = new ExternalAIAgentAutoPilotService(provider, logger);

        await service.ProcessOnceAsync();

        Assert.True(logger.Scopes.Count >= 3);
        Assert.Contains(logger.Scopes, scope => scope.TryGetValue("TenantId", out var value) && Equals(value, tenant.TenantId));
        Assert.Contains(logger.Scopes, scope => scope.TryGetValue("ChangeId", out var value) && Equals(value, change.Id));
        Assert.Contains(logger.Scopes, scope => scope.TryGetValue("PlanId", out var value) && Equals(value, planId));
    }

    private static Tenant CreateAutoPilotTenant()
    {
        var tenant = new Tenant("AutoPilot Tenant");
        tenant.SetExternalAIAgent(true, true, "ops");
        return tenant;
    }

    private static ServiceProvider BuildProvider(
        Tenant tenant,
        IExternalAgentChangeStore changeStore,
        IExternalAIAgentPlanner planner,
        IExternalAIAgentExecutor executor)
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<FlowOSDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped(_ => changeStore);
        services.AddScoped(_ => planner);
        services.AddScoped(_ => executor);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        dbContext.Tenants.Add(tenant);
        dbContext.SaveChanges();
        return provider;
    }

    private sealed class CapturingLogger : ILogger<ExternalAIAgentAutoPilotService>
    {
        public List<Dictionary<string, object?>> Scopes { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                Scopes.Add(pairs.ToDictionary(pair => pair.Key, pair => pair.Value));
            }
            else
            {
                Scopes.Add(new Dictionary<string, object?> { ["State"] = state });
            }

            return new NoOpDisposable();
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
