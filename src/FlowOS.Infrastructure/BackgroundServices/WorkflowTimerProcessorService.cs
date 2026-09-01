using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.BackgroundServices;

public class WorkflowTimerProcessorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorkflowTimerProcessorService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(1);

    public WorkflowTimerProcessorService(IServiceProvider serviceProvider, ILogger<WorkflowTimerProcessorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WorkflowTimerProcessorService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueTimersAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error processing workflow timer jobs.");
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

        _logger.LogInformation("WorkflowTimerProcessorService stopped.");
    }

    private async Task ProcessDueTimersAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var now = DateTime.UtcNow;
        var dueJobs = await dbContext.WorkflowTimerJobs
            .Where(t => !t.IsProcessed && t.DueTimeUtc <= now)
            .OrderBy(t => t.DueTimeUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!dueJobs.Any()) return;

        foreach (var job in dueJobs)
        {
            try
            {
                _logger.LogInformation("Triggering due timer {JobId} for workflow {WorkflowId} with event {EventType}",
                    job.Id, job.WorkflowInstanceId, job.TriggerEventType);

                var command = new PublishEventCommand(
                    job.TenantId,
                    job.WorkflowInstanceId,
                    job.TriggerEventType,
                    null
                );

                await mediator.Send(command, cancellationToken);
                job.MarkAsProcessed();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to execute due timer {JobId} for workflow {WorkflowId}", job.Id, job.WorkflowInstanceId);
                // Mark processed to avoid infinite loop on failed invalid workflow states
                job.MarkAsProcessed();
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
