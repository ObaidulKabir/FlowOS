using System;
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
    private readonly ILogger<WorkflowTimerService> _logger;

    public WorkflowTimerService(FlowOSDbContext dbContext, ILogger<WorkflowTimerService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task ScheduleTimerAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        TimeSpan duration,
        string triggerEventType,
        CancellationToken cancellationToken = default)
    {
        var dueTimeUtc = DateTime.UtcNow.Add(duration);
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
}
