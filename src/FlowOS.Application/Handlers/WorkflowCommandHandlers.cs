using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Workflows.Domain;
using FlowOS.Infrastructure.Persistence;
using MediatR;
using FlowOS.Workflows.Engine;
using FlowOS.StateMachines.Models;
using Microsoft.EntityFrameworkCore;
using FlowOS.Events.Models;
using FlowOS.Application.Commands;
using FlowOS.Core.Interfaces;

namespace FlowOS.Application.Handlers;

public class WorkflowCommandHandlers : 
    IRequestHandler<StartWorkflowCommand, Guid>,
    IRequestHandler<PublishEventCommand, bool>,
    IRequestHandler<CompleteTaskCommand, bool>
{
    private readonly FlowOSDbContext _context;
    private readonly WorkflowEngine _engine;
    private readonly IEventRegistry _eventRegistry; // Added Registry

    public WorkflowCommandHandlers(FlowOSDbContext context, IEventRegistry eventRegistry)
    {
        _context = context;
        _eventRegistry = eventRegistry; // Injected
        _engine = new WorkflowEngine();
    }

    public async Task<Guid> Handle(StartWorkflowCommand request, CancellationToken cancellationToken)
    {
        // 1. Create Instance
        var instance = new WorkflowInstance(
            request.TenantId,
            request.WorkflowDefinitionId,
            request.Version,
            request.InitialStepId,
            request.CorrelationId
        );

        // 2. Persist
        _context.WorkflowInstances.Add(instance);
        await _context.SaveChangesAsync(cancellationToken);

        return instance.Id;
    }

    public async Task<bool> Handle(PublishEventCommand request, CancellationToken cancellationToken)
    {
        // 0. Validate Event ID
        // In Phase 1: We support legacy strings, so we only validate if it looks like an ID or we enforce it.
        // For strictness, let's check if it exists in registry. If yes, it's valid.
        // If no, we assume legacy string (warning logged ideally).
        // BUT, if it IS a new ID, it MUST exist.
        
        var isRegistered = await _eventRegistry.ExistsAsync(request.EventType, request.TenantId);
        if (!isRegistered && request.EventType.StartsWith("EVT-"))
        {
             // It looks like an ID but doesn't exist -> Reject
             return false;
        }

        // 1. Load Workflow Instance
        var instance = await _context.WorkflowInstances
            .FirstOrDefaultAsync(w => w.Id == request.WorkflowInstanceId && w.TenantId == request.TenantId, cancellationToken);

        if (instance == null) return false;

        // 2. Load Definition
        var definition = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(d => d.Id == instance.WorkflowDefinitionId, cancellationToken);
            
        if (definition == null) return false;

        // 3. Create Event Wrapper (Concrete implementation needed for Engine)
        // In a real app, we would persist this event too.
        var domainEvent = new GenericDomainEvent(request.TenantId, request.EventType);
        if (request.CorrelationId.HasValue)
        {
            domainEvent.SetCorrelationId(request.CorrelationId.Value);
        }

        // 4. Advance Workflow
        var result = _engine.Advance(instance, definition, domainEvent, new FlowOS.StateMachines.Models.ExecutionContext());

        if (result.Success)
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    public async Task<bool> Handle(CompleteTaskCommand request, CancellationToken cancellationToken)
    {
        // 1. Load Workflow Instance (Task is part of workflow)
        var instance = await _context.WorkflowInstances
            .FirstOrDefaultAsync(w => w.Id == request.WorkflowInstanceId && w.TenantId == request.TenantId, cancellationToken);

        if (instance == null) return false;

        // 2. Load Definition
        var definition = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(d => d.Id == instance.WorkflowDefinitionId, cancellationToken);

        if (definition == null) return false;

        // 3. Create TaskCompleted Event
        // In this phase, TaskId is the WorkflowInstanceId or the StepId. 
        // We use the request.TaskId which correlates to what the UI sent.
        var domainEvent = new TaskCompleted(request.TenantId, request.TaskId, Guid.Empty); // User ID should come from context, passed as Guid.Empty for now
        
        if (request.CorrelationId.HasValue)
        {
            domainEvent.SetCorrelationId(request.CorrelationId.Value);
        }

        // 4. Advance Workflow via Engine
        // The Engine decides if "TaskCompleted" triggers a transition.
        var result = _engine.Advance(instance, definition, domainEvent, new FlowOS.StateMachines.Models.ExecutionContext());

        if (result.Success)
        {
            // 5. Persist Event & State
            _context.Events.Add(domainEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    // Concrete event for runtime usage
    private class GenericDomainEvent : DomainEvent
    {
        public override string EventType { get; }
        public GenericDomainEvent(Guid tenantId, string eventType) : base(tenantId, eventType)
        {
            EventType = eventType;
        }
    }
}
