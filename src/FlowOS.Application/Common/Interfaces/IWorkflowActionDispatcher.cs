using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Workflows.Domain;

namespace FlowOS.Application.Common.Interfaces;

public interface IWorkflowActionDispatcher
{
    Task QueueActionsAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        string triggerPhase,
        IReadOnlyList<StepActionDefinition> actions,
        Dictionary<string, object>? payload,
        CancellationToken ct = default);
}
