using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Application.Common.Interfaces;

public interface IWorkflowTimerService
{
    Task ScheduleTimerAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        TimeSpan duration,
        string triggerEventType,
        CancellationToken cancellationToken = default);

    Task ScheduleTimerAtAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        DateTime dueTimeUtc,
        string triggerEventType,
        CancellationToken cancellationToken = default);

    Task CancelTimerAsync(
        Guid workflowInstanceId,
        string stepId,
        CancellationToken cancellationToken = default);

    Task<int> ExecuteDueTimersAsync(CancellationToken cancellationToken = default);

    /// <summary>CR-12: unprocessed timer jobs of these instances (used for task due times).</summary>
    Task<IReadOnlyList<PendingTimer>> ListPendingAsync(
        IReadOnlyCollection<Guid> workflowInstanceIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PendingTimer>>(Array.Empty<PendingTimer>());
}

public sealed record PendingTimer(Guid WorkflowInstanceId, string StepId, string TriggerEventType, DateTime DueTimeUtc);
