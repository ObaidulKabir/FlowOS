using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.Services;

public class WorkflowTimerService : IWorkflowTimerService
{
    private readonly FlowOSDbContext _dbContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorkflowTimerService> _logger;
    private readonly TimeProvider _timeProvider;

    public WorkflowTimerService(
        FlowOSDbContext dbContext,
        IServiceProvider serviceProvider,
        ILogger<WorkflowTimerService> logger,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ScheduleTimerAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        TimeSpan duration,
        string triggerEventType,
        CancellationToken cancellationToken = default)
    {
        var dueTimeUtc = _timeProvider.GetUtcNow().UtcDateTime.Add(duration);
        await ScheduleTimerAtAsync(tenantId, workflowInstanceId, stepId, dueTimeUtc, triggerEventType, cancellationToken);
    }

    public async Task ScheduleTimerAtAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        DateTime dueTimeUtc,
        string triggerEventType,
        CancellationToken cancellationToken = default)
    {
        var job = new WorkflowTimerJob(tenantId, workflowInstanceId, stepId, triggerEventType, dueTimeUtc);

        _dbContext.WorkflowTimerJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Scheduled timer job {JobId} for instance {InstanceId}, step '{StepId}', event '{TriggerEvent}' due at {DueTime}",
            job.Id, workflowInstanceId, stepId, triggerEventType, dueTimeUtc);
    }

    public async Task CancelTimerAsync(
        Guid workflowInstanceId,
        string stepId,
        CancellationToken cancellationToken = default)
    {
        var activeJobs = await _dbContext.WorkflowTimerJobs
            .Where(t => t.WorkflowInstanceId == workflowInstanceId && t.StepId == stepId && !t.IsProcessed)
            .ToListAsync(cancellationToken);

        foreach (var job in activeJobs)
        {
            job.MarkAsProcessed();
        }

        if (activeJobs.Any())
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Cancelled {Count} active timer jobs for instance {InstanceId}, step '{StepId}'",
                activeJobs.Count, workflowInstanceId, stepId);
        }
    }

    public async Task<IReadOnlyList<PendingTimer>> ListPendingAsync(
        IReadOnlyCollection<Guid> workflowInstanceIds,
        CancellationToken cancellationToken = default)
    {
        if (workflowInstanceIds.Count == 0) return Array.Empty<PendingTimer>();
        var ids = workflowInstanceIds.Distinct().ToList();
        return await _dbContext.WorkflowTimerJobs
            .AsNoTracking()
            .Where(t => !t.IsProcessed && ids.Contains(t.WorkflowInstanceId))
            .Select(t => new PendingTimer(t.WorkflowInstanceId, t.StepId, t.TriggerEventType, t.DueTimeUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> ExecuteDueTimersAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var dueJobs = await _dbContext.WorkflowTimerJobs
            .Where(t => !t.IsProcessed && t.DueTimeUtc <= now)
            .OrderBy(t => t.DueTimeUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!dueJobs.Any()) return 0;

        var executedCount = 0;
        foreach (var job in dueJobs)
        {
            FlowOS.Core.Telemetry.FlowOsTelemetry.TimerLag.Record(Math.Max(0, (now - job.DueTimeUtc).TotalMilliseconds));
            try
            {
                _logger.LogInformation("Triggering due timer {JobId} for workflow {WorkflowId} with event {EventType}",
                    job.Id, job.WorkflowInstanceId, job.TriggerEventType);

                var command = new FlowOS.Application.Commands.PublishEventCommand(
                    job.TenantId,
                    job.WorkflowInstanceId,
                    job.TriggerEventType,
                    null
                );

                var mediator = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<MediatR.IMediator>(_serviceProvider);
                if (mediator != null)
                {
                    await mediator.Send(command, cancellationToken);
                }

                job.MarkAsProcessed();
                // Save per job, so a later concurrency conflict cannot undo timers already applied.
                await _dbContext.SaveChangesAsync(cancellationToken);
                executedCount++;
            }
            catch (Exception ex) when (ex is FlowOS.Application.Common.Exceptions.ConcurrencyConflictException
                                       || ex is DbUpdateConcurrencyException)
            {
                // Another request changed the instance at the same moment (CR-06). Discard this pass's
                // unsaved changes and stop; the job stays due and the next pass (about a second later)
                // applies the timer to the instance's current state.
                _logger.LogInformation(ex, "Timer {JobId} for workflow {WorkflowId} raced with another update; retrying on the next pass",
                    job.Id, job.WorkflowInstanceId);
                _dbContext.ChangeTracker.Clear();
                return executedCount;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TimerService Exception]: {ex}");
                _logger.LogWarning(ex, "Failed to execute due timer {JobId} for workflow {WorkflowId}", job.Id, job.WorkflowInstanceId);
                job.MarkAsProcessed();
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return executedCount;
    }
}
