using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowOS.MCP.Services;

public sealed class ExternalAIAgentAutoPilotService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExternalAIAgentAutoPilotService> _logger;
    private readonly TimeSpan _pollingInterval;
    private readonly TimeSpan _leaseTtl;
    private readonly TimeSpan _leaseRenewThreshold;
    private readonly TimeSpan _leaseSweepInterval;

    public ExternalAIAgentAutoPilotService(
        IServiceProvider serviceProvider,
        ILogger<ExternalAIAgentAutoPilotService> logger,
        TimeSpan? pollingInterval = null,
        TimeSpan? leaseTtl = null,
        TimeSpan? leaseRenewThreshold = null,
        TimeSpan? leaseSweepInterval = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(2);
        _leaseTtl = leaseTtl ?? TimeSpan.FromSeconds(120);
        _leaseRenewThreshold = leaseRenewThreshold ?? TimeSpan.FromSeconds(60);
        _leaseSweepInterval = leaseSweepInterval ?? TimeSpan.FromSeconds(30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("External AI Agent autopilot service started.");
        EnsureToolsRegistered();

        var sweepTask = RunLeaseSweepAsync(stoppingToken);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "External AI Agent autopilot iteration failed.");
                }

                try
                {
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            try
            {
                await sweepTask;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path.
            }

            McpRequestContext.Clear();
            _logger.LogInformation("External AI Agent autopilot service stopped.");
        }
    }

    public async Task ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();

        var tenants = await dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.ExternalAIAgentEnabled == true && t.ExternalAIAgentAutoPilot == true)
            .OrderBy(t => t.TenantId)
            .Select(t => new AutoPilotTenant(t.TenantId, t.ExternalAIAgentProfileId))
            .ToListAsync(cancellationToken);

        foreach (var tenant in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessTenantAsync(tenant, cancellationToken);
        }
    }

    private async Task ProcessTenantAsync(AutoPilotTenant tenant, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var changeStore = scope.ServiceProvider.GetRequiredService<IExternalAgentChangeStore>();
        var planner = scope.ServiceProvider.GetRequiredService<IExternalAIAgentPlanner>();
        var executor = scope.ServiceProvider.GetRequiredService<IExternalAIAgentExecutor>();
        var tenantId = tenant.TenantId;
        var agentId = $"autopilot:{tenantId:N}";

        using var tenantScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = tenantId
        });

        var leased = await changeStore.LeaseNextAsync(tenantId, agentId, _leaseTtl, 1, cancellationToken);
        var change = leased.FirstOrDefault();
        if (change == null)
            return;

        using var changeScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = tenantId,
            ["ChangeId"] = change.Id
        });

        var startedAtUtc = DateTime.UtcNow;
        SetAutopilotRequestContext(tenantId);

        try
        {
            _logger.LogInformation("Autopilot leased external agent change.");

            var plan = await planner.PlanAsync(
                change.Id,
                tenantId,
                tenant.AgentProfileId,
                null,
                cancellationToken);

            await RenewLeaseIfNeededAsync(changeStore, change.Id, change.LeasedByAgent ?? agentId, startedAtUtc, cancellationToken);

            using var planScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["ChangeId"] = change.Id,
                ["PlanId"] = plan.PlanId
            });

            _logger.LogInformation("Autopilot created external agent plan with {StepCount} step(s).", plan.Steps.Count);

            var execution = await executor.ExecutePlanAsync(plan.PlanId, tenantId, cancellationToken);

            await RenewLeaseIfNeededAsync(changeStore, change.Id, change.LeasedByAgent ?? agentId, startedAtUtc, cancellationToken);

            var failureSummary = execution.Success ? null : BuildFailureSummary(execution);
            await changeStore.AckAsync(change.Id, execution.Success, failureSummary, cancellationToken);

            _logger.LogInformation(
                "Autopilot acknowledged external agent change with success={Success}.",
                execution.Success);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Autopilot failed while processing external agent change.");
            await changeStore.AckAsync(change.Id, false, ex.Message, cancellationToken);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    private async Task RunLeaseSweepAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_leaseSweepInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var changeStore = scope.ServiceProvider.GetRequiredService<IExternalAgentChangeStore>();
                var reclaimed = await changeStore.ReclaimExpiredLeasesAsync(cancellationToken);
                if (reclaimed > 0)
                {
                    _logger.LogInformation("Autopilot reclaimed {LeaseCount} expired external agent lease(s).", reclaimed);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Autopilot lease sweep failed.");
            }
        }
    }

    private async Task RenewLeaseIfNeededAsync(
        IExternalAgentChangeStore changeStore,
        Guid changeId,
        string agentId,
        DateTime startedAtUtc,
        CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - startedAtUtc < _leaseRenewThreshold)
            return;

        await changeStore.RenewLeaseAsync(changeId, agentId, _leaseTtl, cancellationToken);
        _logger.LogInformation("Autopilot renewed lease for external agent change.");
    }

    private void EnsureToolsRegistered()
    {
        var registry = _serviceProvider.GetRequiredService<IToolRegistry>();
        ToolRegistration.RegisterAll(registry, _serviceProvider);
    }

    private static string BuildFailureSummary(ExternalAIAgentExecutionResult execution)
    {
        var failedSteps = execution.StepResults
            .Where(step => !string.Equals(step.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            .Select(step => $"{step.StepId}:{step.Status}{(string.IsNullOrWhiteSpace(step.ErrorMessage) ? string.Empty : $" ({step.ErrorMessage})")}")
            .ToList();

        return failedSteps.Count == 0
            ? "Plan execution reported failure."
            : string.Join("; ", failedSteps);
    }

    private static void SetAutopilotRequestContext(Guid tenantId)
    {
        McpRequestContext.TenantId = tenantId;
        McpRequestContext.Role = "Admin";
        McpRequestContext.Scopes = Array.Empty<string>();
        McpRequestContext.IsApiKey = false;
        McpRequestContext.IsAuthenticatedTransport = true;
    }

    private sealed record AutoPilotTenant(Guid TenantId, string? AgentProfileId);
}
