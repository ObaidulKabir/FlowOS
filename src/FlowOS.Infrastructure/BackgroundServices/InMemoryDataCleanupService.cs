using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.BackgroundServices;

/// <summary>
/// Background service that automatically purges expired temporary in-memory data
/// (workflow instances, events) older than a configurable TTL (default: 4 hours, customizable 2-6 hours).
/// </summary>
public class InMemoryDataCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InMemoryDataCleanupService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(15);

    public InMemoryDataCleanupService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<InMemoryDataCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InMemoryDataCleanupService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredInMemoryDataAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error executing in-memory data cleanup.");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("InMemoryDataCleanupService stopped.");
    }

    private async Task PurgeExpiredInMemoryDataAsync(CancellationToken cancellationToken)
    {
        bool useInMemory = _configuration.GetValue<bool>("UseInMemoryDatabase");
        bool autoCleanupEnabled = _configuration.GetValue<bool>("Sandbox:AutoCleanupEnabled", true);

        if (!useInMemory && !autoCleanupEnabled)
        {
            return;
        }

        int ttlHours = _configuration.GetValue<int>("Sandbox:DataTtlHours", 4);
        if (ttlHours < 1) ttlHours = 4; // fallback safety default

        var cutoffTime = DateTime.UtcNow.AddHours(-ttlHours);

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();

        // Purge expired instances
        var expiredInstances = await dbContext.WorkflowInstances
            .Where(w => w.CreatedAt < cutoffTime)
            .ToListAsync(cancellationToken);

        if (expiredInstances.Any())
        {
            dbContext.WorkflowInstances.RemoveRange(expiredInstances);
        }

        // Purge expired events
        var expiredEvents = await dbContext.Events
            .Where(e => e.Timestamp < cutoffTime)
            .ToListAsync(cancellationToken);

        if (expiredEvents.Any())
        {
            dbContext.Events.RemoveRange(expiredEvents);
        }

        if (expiredInstances.Any() || expiredEvents.Any())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "[SandboxCleanup] Purged {InstanceCount} expired workflow instances and {EventCount} events created prior to {CutoffTime} (TTL: {TtlHours} hours).",
                expiredInstances.Count,
                expiredEvents.Count,
                cutoffTime,
                ttlHours);
        }
    }
}
