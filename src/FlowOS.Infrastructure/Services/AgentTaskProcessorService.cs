using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowOS.Infrastructure.Services;

public sealed class AgentTaskProcessorOptions
{
    public const string ConfigurationSection = "FlowOS:Agents:Worker";

    public bool Enabled { get; set; } = true;
    public bool ExecuteInMcpRequests { get; set; } = true;
    public int BatchSize { get; set; } = 10;
    public int PollIntervalMilliseconds { get; set; } = 500;
    public int ClaimDurationSeconds { get; set; } = 120;
    public int MaxParallelism { get; set; } = 4;
    public int CleanupIntervalMinutes { get; set; } = 60;
}

public sealed class AgentTaskProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentTaskProcessorService> _logger;
    private readonly AgentTaskProcessorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _claimant;

    public AgentTaskProcessorService(
        IServiceScopeFactory scopeFactory,
        IOptions<AgentTaskProcessorOptions> options,
        ILogger<AgentTaskProcessorService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _claimant = $"agent:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(
            Math.Clamp(_options.PollIntervalMilliseconds, 25, 60_000));
        var claimDuration = TimeSpan.FromSeconds(
            Math.Clamp(_options.ClaimDurationSeconds, 5, 3600));
        var cleanupInterval = TimeSpan.FromMinutes(
            Math.Clamp(_options.CleanupIntervalMinutes, 1, 24 * 60));
        var nextCleanupAtUtc = UtcNow();

        _logger.LogInformation("Durable agent task processor started as {Claimant}.", _claimant);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claims = await ClaimCycleAsync(claimDuration, stoppingToken);
                if (claims.Count > 0)
                {
                    await Parallel.ForEachAsync(
                        claims,
                        new ParallelOptions
                        {
                            CancellationToken = stoppingToken,
                            MaxDegreeOfParallelism = Math.Clamp(_options.MaxParallelism, 1, 100)
                        },
                        (claim, token) => ProcessClaimAsync(claim, claimDuration, token));
                }

                if (UtcNow() >= nextCleanupAtUtc)
                {
                    await CleanupAsync(stoppingToken);
                    nextCleanupAtUtc = UtcNow().Add(cleanupInterval);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Durable agent task processor cycle failed.");
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Durable agent task processor stopped.");
    }

    private async Task<IReadOnlyList<AgentTaskClaim>> ClaimCycleAsync(
        TimeSpan claimDuration,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IAgentTaskQueue>();
        await queue.ReconcileAsync(cancellationToken);
        return await queue.ClaimBatchAsync(
            _claimant,
            Math.Min(
                Math.Clamp(_options.BatchSize, 1, 1000),
                Math.Clamp(_options.MaxParallelism, 1, 100)),
            claimDuration,
            cancellationToken);
    }

    private async ValueTask ProcessClaimAsync(
        AgentTaskClaim claim,
        TimeSpan claimDuration,
        CancellationToken cancellationToken)
    {
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewal = RenewClaimUntilDoneAsync(
            claim,
            claimDuration,
            execution,
            heartbeat.Token);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetService<AgentWorkerCurrentUser>()?.SetTenant(claim.TenantId);
            var coordinator = scope.ServiceProvider.GetRequiredService<IAgentTaskCoordinator>();
            await coordinator.ProcessClaimAsync(claim, execution.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Durable agent job {JobId} failed outside the coordinator.", claim.JobId);
        }
        finally
        {
            heartbeat.Cancel();
            await renewal;
        }
    }

    private async Task RenewClaimUntilDoneAsync(
        AgentTaskClaim claim,
        TimeSpan claimDuration,
        CancellationTokenSource execution,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(claimDuration.TotalSeconds / 3, 1, 30));
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var queue = scope.ServiceProvider.GetRequiredService<IAgentTaskQueue>();
                if (await queue.RenewClaimAsync(
                        claim.JobId, claim.Claimant, claimDuration, cancellationToken))
                    continue;

                _logger.LogWarning("Lost ownership of agent job {JobId}; cancelling execution.", claim.JobId);
                execution.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not renew agent job {JobId}; cancelling execution.", claim.JobId);
            execution.Cancel();
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var cleanup = scope.ServiceProvider.GetRequiredService<IAgentPersistenceCleanupService>();
            var result = await cleanup.CleanupAsync(cancellationToken);
            if (result.JobsDeleted +
                result.LeasesDeleted +
                result.UsageRowsDeleted +
                result.ExecutionRecordsDeleted > 0)
            {
                _logger.LogInformation(
                    "Agent retention cleanup removed {Jobs} jobs, {Leases} leases, {Usage} usage rows, and {Executions} execution records.",
                    result.JobsDeleted,
                    result.LeasesDeleted,
                    result.UsageRowsDeleted,
                    result.ExecutionRecordsDeleted);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Agent retention cleanup failed; processing will continue.");
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
