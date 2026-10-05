using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Events.Models;
using FlowOS.Workflows.Enums;

namespace FlowOS.Application.Handlers;

public partial class WorkflowCommandHandlers
{
    public async Task<bool> Handle(AssignInstanceRoleCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RoleName))
            throw new ArgumentException("roleName is required.");
        if (string.IsNullOrWhiteSpace(request.AssigneeId))
            throw new ArgumentException("assigneeId is required.");

        var instance = await _unitOfWork.WorkflowInstances
            .GetByIdAsync(request.WorkflowInstanceId, request.TenantId, cancellationToken);
        if (instance == null)
            throw new KeyNotFoundException($"Workflow instance '{request.WorkflowInstanceId}' was not found.");

        if (instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed)
        {
            throw new ArgumentException(
                $"Cannot assign a business role on a {instance.Status} workflow instance.");
        }

        var definition = await _unitOfWork.WorkflowDefinitions
            .GetByIdAsync(instance.WorkflowDefinitionId, cancellationToken);
        if (definition == null)
            throw new KeyNotFoundException($"Workflow definition '{instance.WorkflowDefinitionId}' was not found.");

        var role = definition.BusinessRoles.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, request.RoleName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (role == null)
        {
            throw new ArgumentException(
                $"Role '{request.RoleName.Trim()}' is not a business role declared on this workflow.");
        }

        var resolutionType = string.IsNullOrWhiteSpace(role.ResolutionType) ? "Assignment" : role.ResolutionType.Trim();
        if (!string.Equals(resolutionType, "Assignment", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Role '{role.Name}' uses resolutionType {resolutionType}. assign_instance_role only writes Assignment roles. Expression is read from the business payload, and Static uses staticMembers.");
        }

        instance.AssignRole(role.Name, request.AssigneeId.Trim());

        var assigned = new StandardEvent(request.TenantId, "RoleAssigned");
        assigned.SetCorrelationId(instance.Id);
        assigned.AddMetadata("RoleName", role.Name);
        assigned.AddMetadata("AssigneeId", request.AssigneeId.Trim());
        assigned.AddMetadata("StepId", instance.CurrentStepId);
        AssignActorMetadata(assigned);
        _unitOfWork.Events.Add(assigned);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
