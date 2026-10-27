using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Events.Models;
using FlowOS.Workflows.Enums;

namespace FlowOS.Application.Handlers;

public partial class WorkflowCommandHandlers
{
    public async Task<bool> Handle(SetWorkflowPriorityCommand request, CancellationToken cancellationToken)
    {
        var instance = await _unitOfWork.WorkflowInstances
            .GetByIdAsync(request.WorkflowInstanceId, request.TenantId, cancellationToken);
        if (instance == null)
            throw new KeyNotFoundException($"Workflow instance '{request.WorkflowInstanceId}' was not found.");

        if (instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed)
            throw new ArgumentException($"Cannot change the priority of a {instance.Status} workflow instance.");

        instance.SetPriority(request.Priority);

        var changed = new StandardEvent(request.TenantId, "PriorityChanged");
        changed.SetCorrelationId(instance.Id);
        changed.AddMetadata("WorkflowInstanceId", instance.Id.ToString());
        changed.AddMetadata("Priority", request.Priority?.ToString() ?? string.Empty);
        changed.AddMetadata("StepId", instance.CurrentStepId);
        AssignActorMetadata(changed);
        _unitOfWork.Events.Add(changed);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
