using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using FlowOS.Core.Security;
using FlowOS.Workflows.Enums;
using MediatR;

namespace FlowOS.Application.Handlers;

public class TaskQueryHandlers :
    IRequestHandler<GetTasksQuery, List<TaskDto>>,
    IRequestHandler<GetTaskByIdQuery, TaskDto?>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly FlowOS.Application.Common.Interfaces.IWorkflowTimerService? _timerService;

    public TaskQueryHandlers(IUnitOfWork unitOfWork, FlowOS.Application.Common.Interfaces.IWorkflowTimerService? timerService = null)
    {
        _unitOfWork = unitOfWork;
        _timerService = timerService;
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
        var pendingTimers = _timerService == null
            ? new List<FlowOS.Application.Common.Interfaces.PendingTimer>()
            : (await _timerService.ListPendingAsync(workflowIds, cancellationToken)).ToList();

        var tasks = workflows
            .Select(w =>
            {
                var requiredRoles = ResolveRequiredRoles(w.WorkflowDefinitionId, w.CurrentStepId, definitionsById);
                var step = definitionsById.TryGetValue(w.WorkflowDefinitionId, out var def)
                    ? def.Steps.FirstOrDefault(s => s.StepId == w.CurrentStepId)
                    : null;
                return new TaskDto
                {
                    TaskId = w.Id,
                    WorkflowId = w.WorkflowDefinitionId,
                    CurrentStep = w.CurrentStepId,
                    Status = w.Status.ToString(),
                    Priority = w.Priority ?? step?.Priority ?? 0,
                    DueAtUtc = ResolveDueAt(w.Id, w.CurrentStepId, step?.Sla?.TimeoutEvent, pendingTimers),
                    CreatedAt = w.CreatedAt,
                    RequiredRole = requiredRoles.FirstOrDefault() ?? "User",
                    RequiredRoles = requiredRoles,
                    AssigneeId = ResolveAssigneeId(requiredRoles, w.RoleAssignments),
                    RoleAssignments = CopyAssignments(w.RoleAssignments),
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
            })
            .Where(task => CallerCanSeeTask(task.RequiredRoles, request.CallerRoles))
            .Where(task => string.IsNullOrWhiteSpace(request.Role) ||
                           task.RequiredRoles.Contains(request.Role.Trim(), StringComparer.OrdinalIgnoreCase))
            .Where(task => !request.MinPriority.HasValue || task.Priority >= request.MinPriority.Value)
            .Where(task => !request.DueBeforeUtc.HasValue || (task.DueAtUtc.HasValue && task.DueAtUtc.Value <= request.DueBeforeUtc.Value));

        return OrderForInbox(tasks).ToList();
    }

    /// <summary>
    /// CR-12: deterministic inbox order. Higher priority first, then the earliest SLA due time (tasks without
    /// one last), then the oldest, then the task id.
    /// </summary>
    public static IEnumerable<TaskDto> OrderForInbox(IEnumerable<TaskDto> tasks) => tasks
        .OrderByDescending(t => t.Priority)
        .ThenBy(t => t.DueAtUtc.HasValue ? 0 : 1)
        .ThenBy(t => t.DueAtUtc)
        .ThenBy(t => t.CreatedAt)
        .ThenBy(t => t.TaskId);

    private static DateTime? ResolveDueAt(
        Guid instanceId,
        string stepId,
        string? timeoutEvent,
        IReadOnlyList<FlowOS.Application.Common.Interfaces.PendingTimer> timers)
    {
        if (string.IsNullOrWhiteSpace(timeoutEvent)) return null;
        var due = timers
            .Where(t => t.WorkflowInstanceId == instanceId &&
                        t.StepId == stepId &&
                        string.Equals(t.TriggerEventType, timeoutEvent, StringComparison.OrdinalIgnoreCase))
            .Select(t => (DateTime?)t.DueTimeUtc)
            .Min();
        return due.HasValue ? DateTime.SpecifyKind(due.Value, DateTimeKind.Utc) : null;
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
        var requiredRoles = definition?.Steps
            .FirstOrDefault(x => x.StepId == workflow.CurrentStepId)?
            .AllowedRoles
            .ToList() ?? new List<string>();

        if (!CallerCanSeeTask(requiredRoles, request.CallerRoles))
            return null;

        var currentStep = definition?.Steps.FirstOrDefault(x => x.StepId == workflow.CurrentStepId);
        var timers = _timerService == null
            ? (IReadOnlyList<FlowOS.Application.Common.Interfaces.PendingTimer>)Array.Empty<FlowOS.Application.Common.Interfaces.PendingTimer>()
            : await _timerService.ListPendingAsync(new[] { workflow.Id }, cancellationToken);

        return new TaskDto
        {
            TaskId = workflow.Id,
            WorkflowId = workflow.WorkflowDefinitionId,
            CurrentStep = workflow.CurrentStepId,
            Status = workflow.Status.ToString(),
            Priority = workflow.Priority ?? currentStep?.Priority ?? 0,
            DueAtUtc = ResolveDueAt(workflow.Id, workflow.CurrentStepId, currentStep?.Sla?.TimeoutEvent, timers),
            CreatedAt = workflow.CreatedAt,
            RequiredRole = requiredRoles.FirstOrDefault() ?? "User",
            RequiredRoles = requiredRoles,
            AssigneeId = ResolveAssigneeId(requiredRoles, workflow.RoleAssignments),
            RoleAssignments = CopyAssignments(workflow.RoleAssignments),
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
        if (!definitions.TryGetValue(definitionId, out var definition))
            return new List<string>();

        return definition.Steps
            .FirstOrDefault(x => x.StepId == currentStepId)?
            .AllowedRoles
            .ToList() ?? new List<string>();
    }

    internal static bool CallerCanSeeTask(IReadOnlyList<string> requiredRoles, IReadOnlyList<string>? callerRoles)
    {
        if (callerRoles != null && callerRoles.Any(TenantIdentityRules.IsPrivilegedRole))
            return true;

        if (requiredRoles.Count == 0)
            return true;

        if (callerRoles == null || callerRoles.Count == 0)
            return false;

        return requiredRoles.Any(required =>
            callerRoles.Any(caller => string.Equals(caller, required, StringComparison.OrdinalIgnoreCase)));
    }

    private static Dictionary<string, string> CopyAssignments(IReadOnlyDictionary<string, string>? assignments)
        => assignments == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(assignments, StringComparer.OrdinalIgnoreCase);

    private static string? ResolveAssigneeId(
        IReadOnlyList<string> requiredRoles,
        IReadOnlyDictionary<string, string>? assignments)
    {
        if (assignments == null || assignments.Count == 0)
            return null;

        foreach (var role in requiredRoles)
        {
            if (assignments.TryGetValue(role, out var assignee) && !string.IsNullOrWhiteSpace(assignee))
                return assignee;
        }

        return null;
    }
}
