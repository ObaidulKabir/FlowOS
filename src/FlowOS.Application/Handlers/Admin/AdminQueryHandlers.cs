using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Agents.Events;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.DTOs.Admin;
using FlowOS.Application.Queries.Admin;
using FlowOS.Events.Models;
using MediatR;
using FlowOS.Domain.Entities;
using FlowOS.Security.Policies;

namespace FlowOS.Application.Handlers.Admin;

public class AdminQueryHandlers :
    IRequestHandler<GetAdminWorkflowDetailQuery, AdminWorkflowDetailDto>,
    IRequestHandler<GetAdminStateMachineQuery, AdminStateMachineDto>,
    IRequestHandler<GetAllAdminStateMachinesQuery, List<AdminStateMachineDto>>,
    IRequestHandler<GetAdminPoliciesQuery, List<AdminPolicyDto>>,
    IRequestHandler<GetAdminEventsQuery, List<AdminEventDefinitionDto>>,
    IRequestHandler<GetAdminWorkflowsQuery, List<AdminWorkflowSummaryDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPolicyProvider _policyProvider;

    public AdminQueryHandlers(IUnitOfWork unitOfWork, IPolicyProvider policyProvider)
    {
        _unitOfWork = unitOfWork;
        _policyProvider = policyProvider;
    }

    public async Task<List<AdminWorkflowSummaryDto>> Handle(GetAdminWorkflowsQuery request, CancellationToken cancellationToken)
    {
        var instances = await _unitOfWork.WorkflowInstances
            .ListByTenantAsync(request.TenantId, cancellationToken);

        var defIds = instances.Select(i => i.WorkflowDefinitionId).Distinct().ToList();
        var definitions = await _unitOfWork.WorkflowDefinitions
            .GetNamesByIdsAsync(defIds, cancellationToken);

        return instances.Select(i => new AdminWorkflowSummaryDto
        {
            Id = i.Id,
            DefinitionId = i.WorkflowDefinitionId,
            DefinitionName = definitions.ContainsKey(i.WorkflowDefinitionId) ? definitions[i.WorkflowDefinitionId] : "Unknown",
            Version = i.WorkflowVersion,
            CurrentStepId = i.CurrentStepId,
            Status = i.Status.ToString(),
            CorrelationId = i.CorrelationId
        }).ToList();
    }

    public async Task<List<AdminEventDefinitionDto>> Handle(GetAdminEventsQuery request, CancellationToken cancellationToken)
    {
        var events = await _unitOfWork.EventDefinitions
            .ListByTenantAsync(request.TenantId, cancellationToken);

        return events.Select(e => new AdminEventDefinitionDto
        {
            EventId = e.EventId,
            DisplayName = e.Name,
            Description = e.Description,
            EntityType = e.EntityType,
            Category = e.Category.ToString()
        }).ToList();
    }

    public async Task<List<AdminPolicyDto>> Handle(GetAdminPoliciesQuery request, CancellationToken cancellationToken)
    {
        var policies = await _policyProvider.GetAllPoliciesAsync();
        
        return policies.Select(p => new AdminPolicyDto
        {
            PolicyName = p.Name,
            Scope = p.Scope,
            Description = p.Description,
            IsEnabled = true,
            DenyReasonTemplate = "Action denied by policy",
            Rules = new List<string> { "Allow All" }
        }).ToList();
    }

    public async Task<AdminStateMachineDto> Handle(GetAdminStateMachineQuery request, CancellationToken cancellationToken)
    {
        var definition = await _unitOfWork.StateMachines
            .GetByEntityTypeAsync(request.EntityType, cancellationToken);

        if (definition == null) return null!;

        return MapToStateMachineDto(definition);
    }
    
    public async Task<List<AdminStateMachineDto>> Handle(GetAllAdminStateMachinesQuery request, CancellationToken cancellationToken)
    {
         var definitions = await _unitOfWork.StateMachines.ListAllAsync(cancellationToken);
         return definitions.Select(MapToStateMachineDto).ToList();
    }

    private AdminStateMachineDto MapToStateMachineDto(StateMachineDefinition def)
    {
        return new AdminStateMachineDto
        {
            EntityType = def.EntityType,
            Version = def.Version.ToString(),
            States = def.States.ToList(),
            Transitions = def.Transitions.Select(t => new AdminTransitionDto
            {
                FromState = t.FromState,
                ToState = t.ToState,
                TriggerEvent = t.TriggerEventType,
                EventId = t.EventId
            }).ToList()
        };
    }

    public async Task<AdminWorkflowDetailDto> Handle(GetAdminWorkflowDetailQuery request, CancellationToken cancellationToken)
    {
        var instance = await _unitOfWork.WorkflowInstances
            .GetByIdAsNoTrackingAsync(request.WorkflowInstanceId, request.TenantId, cancellationToken);

        if (instance == null) return null!;

        var definition = await _unitOfWork.WorkflowDefinitions
            .GetByIdAsNoTrackingAsync(instance.WorkflowDefinitionId, cancellationToken);

        var events = await _unitOfWork.Events
            .ListByCorrelationIdAsync(request.WorkflowInstanceId, cancellationToken);

        var timeline = events.Select(MapToTimeline).ToList();

        return new AdminWorkflowDetailDto
        {
            Id = instance.Id,
            DefinitionId = instance.WorkflowDefinitionId,
            DefinitionName = definition?.Name ?? "Unknown",
            Version = instance.WorkflowVersion,
            CurrentStepId = instance.CurrentStepId,
            Status = instance.Status.ToString(),
            CorrelationId = instance.CorrelationId,
            Timeline = timeline
        };
    }

    private AdminTimelineEventDto MapToTimeline(DomainEvent evt)
    {
        var summary = "System event recorded";
        var keyData = new Dictionary<string, string>();

        switch (evt)
        {
            case AgentInsightGenerated aig:
                summary = $"Agent {aig.AgentId} suggested: {aig.Insight}";
                keyData.Add("Agent", aig.AgentId);
                keyData.Add("Objective", aig.ContextObjective);
                break;
            case TaskCompleted tc:
                summary = "Task completed by user";
                keyData.Add("TaskId", tc.TaskId.ToString());
                keyData.Add("UserId", tc.CompletedBy.ToString());
                break;
            default:
                summary = $"Event: {evt.EventType}";
                if (evt.Metadata != null)
                {
                    foreach (var kvp in evt.Metadata)
                    {
                        if (!keyData.ContainsKey(kvp.Key))
                        {
                            keyData.Add(kvp.Key, kvp.Value);
                        }
                    }
                }
                break;
        }

        return new AdminTimelineEventDto
        {
            EventId = evt.EventId,
            EventType = evt.EventType,
            Timestamp = evt.Timestamp,
            Summary = summary,
            KeyData = keyData
        };
    }
}
