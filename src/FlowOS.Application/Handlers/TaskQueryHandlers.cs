using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using FlowOS.Workflows.Enums;
using MediatR;

namespace FlowOS.Application.Handlers;

public class TaskQueryHandlers : 
    IRequestHandler<GetTasksQuery, List<TaskDto>>,
    IRequestHandler<GetTaskByIdQuery, TaskDto?>
{
    private readonly IUnitOfWork _unitOfWork;

    public TaskQueryHandlers(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<List<TaskDto>> Handle(GetTasksQuery request, CancellationToken cancellationToken)
    {
        var workflows = await _unitOfWork.WorkflowInstances
            .ListByStatusAsync(WorkflowInstanceStatus.Waiting, request.TenantId, cancellationToken);
        
        var workflowIds = workflows.Select(w => w.Id).ToList();
        var insights = await _unitOfWork.AgentInsights
            .ListByWorkflowInstanceIdsAsync(workflowIds, cancellationToken);
        var definitions = await _unitOfWork.WorkflowDefinitions
            .GetByIdsAsync(workflows.Select(w => w.WorkflowDefinitionId), cancellationToken);
        var definitionsById = definitions.ToDictionary(x => x.Id);

        return workflows.Select(w =>
        {
            var requiredRoles = ResolveRequiredRoles(w.WorkflowDefinitionId, w.CurrentStepId, definitionsById);
            return new TaskDto
            {
                TaskId = w.Id,
                WorkflowId = w.WorkflowDefinitionId,
                CurrentStep = w.CurrentStepId,
                Status = w.Status.ToString(),
                RequiredRole = requiredRoles.FirstOrDefault() ?? "User",
                RequiredRoles = requiredRoles,
                AgentInsights = insights
                    .Where(i => i.WorkflowInstanceId == w.Id)
                    .Select(i => new AgentInsightDto
                    {
                        AgentId = i.AgentId,
                        Insight = i.Insight,
                        ContextObjective = i.ContextObjective,
                        CreatedAt = i.CreatedAt
                    })
                    .ToList()
            };
        }).ToList();
    }

    public async Task<TaskDto?> Handle(GetTaskByIdQuery request, CancellationToken cancellationToken)
    {
        var workflow = await _unitOfWork.WorkflowInstances
            .GetByIdAsNoTrackingAsync(request.TaskId, request.TenantId, cancellationToken);

        if (workflow == null)
            return null;

        var insights = await _unitOfWork.AgentInsights
            .ListByWorkflowInstanceIdAsync(request.TaskId, cancellationToken);
        var definition = await _unitOfWork.WorkflowDefinitions
            .GetByIdAsNoTrackingAsync(workflow.WorkflowDefinitionId, cancellationToken);
        var requiredRoles = definition?.ContextBindingRevisionId.HasValue == true
            ? definition.Steps
                .FirstOrDefault(x => x.StepId == workflow.CurrentStepId)?
                .AllowedRoles
                .ToList() ?? new List<string>()
            : new List<string>();

        return new TaskDto
        {
            TaskId = workflow.Id,
            WorkflowId = workflow.WorkflowDefinitionId,
            CurrentStep = workflow.CurrentStepId,
            Status = workflow.Status.ToString(),
            RequiredRole = requiredRoles.FirstOrDefault() ?? "User",
            RequiredRoles = requiredRoles,
            AgentInsights = insights.Select(i => new AgentInsightDto
            {
                AgentId = i.AgentId,
                Insight = i.Insight,
                ContextObjective = i.ContextObjective,
                CreatedAt = i.CreatedAt
            }).ToList()
        };
    }

    private static List<string> ResolveRequiredRoles(
        Guid definitionId,
        string currentStepId,
        IReadOnlyDictionary<Guid, FlowOS.Workflows.Domain.WorkflowDefinition> definitions)
    {
        if (!definitions.TryGetValue(definitionId, out var definition) ||
            !definition.ContextBindingRevisionId.HasValue)
        {
            return new List<string>();
        }

        return definition.Steps
            .FirstOrDefault(x => x.StepId == currentStepId)?
            .AllowedRoles
            .ToList() ?? new List<string>();
    }
}
