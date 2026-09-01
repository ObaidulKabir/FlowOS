using System;
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

    Task CancelTimerAsync(
        Guid workflowInstanceId,
        string stepId,
        CancellationToken cancellationToken = default);

    Task<int> ExecuteDueTimersAsync(CancellationToken cancellationToken = default);
}
