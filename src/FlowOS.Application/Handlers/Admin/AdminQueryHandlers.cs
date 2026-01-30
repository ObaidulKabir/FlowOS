using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Agents.Events;
using FlowOS.Application.DTOs.Admin;
using FlowOS.Application.Queries.Admin;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

using FlowOS.Domain.Entities;
using FlowOS.Security.Policies; // Add this

namespace FlowOS.Application.Handlers.Admin;

public class AdminQueryHandlers :
    IRequestHandler<GetAdminWorkflowDetailQuery, AdminWorkflowDetailDto>,
    IRequestHandler<GetAdminStateMachineQuery, AdminStateMachineDto>,
    IRequestHandler<GetAllAdminStateMachinesQuery, List<AdminStateMachineDto>>,
    IRequestHandler<GetAdminPoliciesQuery, List<AdminPolicyDto>>,
    IRequestHandler<GetAdminEventsQuery, List<AdminEventDefinitionDto>>,
    IRequestHandler<GetAdminWorkflowsQuery, List<AdminWorkflowSummaryDto>> // Added Workflow List Handler
{
    private readonly FlowOSDbContext _context;
    private readonly IPolicyProvider _policyProvider;

    public AdminQueryHandlers(FlowOSDbContext context, IPolicyProvider policyProvider)
    {
        _context = context;
        _policyProvider = policyProvider;
    }

    public async Task<List<AdminWorkflowSummaryDto>> Handle(GetAdminWorkflowsQuery request, CancellationToken cancellationToken)
    {
        var instances = await _context.WorkflowInstances
            .AsNoTracking()
            .Where(w => w.TenantId == request.TenantId)
            .OrderByDescending(w => w.Id) // or CreatedAt if available
            .ToListAsync(cancellationToken);

        // Optimization: Fetch definition names in bulk or assume consistency?
        // For simple list, we can fetch definitions or just map what we have.
        // Let's do a simple join-like lookup or just load definition name if EF navigation property exists (it doesn't in clean DDD often).
        // For MVP, we'll fetch definition names separately or rely on frontend to map IDs.
        // Or better, let's just fetch IDs for now to keep it fast.
        
        // Actually, let's try to get Definition Names.
        var defIds = instances.Select(i => i.WorkflowDefinitionId).Distinct().ToList();
        var definitions = await _context.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => defIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

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
        var events = await _context.EventDefinitions
            .AsNoTracking()
            .Where(e => e.TenantId == request.TenantId)
            .OrderBy(e => e.EventId)
            .ToListAsync(cancellationToken);

        return events.Select(e => new AdminEventDefinitionDto
        {
            EventId = e.EventId,
            DisplayName = e.Name, // Fixed from DisplayName to Name
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
            IsEnabled = true, // Simplified for this provider
            DenyReasonTemplate = "Action denied by policy",
            Rules = new List<string> { "Allow All" } // Placeholder
        }).ToList();
    }

    public async Task<AdminStateMachineDto> Handle(GetAdminStateMachineQuery request, CancellationToken cancellationToken)
    {
        var definition = await _context.StateMachineDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(sm => sm.EntityType == request.EntityType, cancellationToken); // Assuming EntityType is unique for now or latest

        if (definition == null) return null;

        return MapToStateMachineDto(definition);
    }
    
    public async Task<List<AdminStateMachineDto>> Handle(GetAllAdminStateMachinesQuery request, CancellationToken cancellationToken)
    {
         var definitions = await _context.StateMachineDefinitions
            .AsNoTracking()
            .ToListAsync(cancellationToken);

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
                TriggerEvent = t.TriggerEventType, // Legacy
                EventId = t.EventId // New
            }).ToList()
        };
    }

    public async Task<AdminWorkflowDetailDto> Handle(GetAdminWorkflowDetailQuery request, CancellationToken cancellationToken)
    {
        var instance = await _context.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WorkflowInstanceId && w.TenantId == request.TenantId, cancellationToken);

        if (instance == null) return null;

        var definition = await _context.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == instance.WorkflowDefinitionId, cancellationToken);

        // Fetch Raw Events - In a real system this might be a separate event store query
        // We use correlation ID or explicit linking. 
        // For Phase 1-6, we assume events have CorrelationId = WorkflowInstanceId or similar.
        // EF Core 8+ can handle simple IS checks but complexities arise. We fetch by correlation ID first.
        var events = await _context.Events
            .AsNoTracking()
            .Where(e => e.CorrelationId == request.WorkflowInstanceId)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(cancellationToken);
            
        // Also try to find TaskCompleted events if they are not correlated (though they should be)
        // For this demo, we assume strict correlation.

        // Map to Timeline
        var timeline = events.Select(e => MapToTimeline(e)).ToList();

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
