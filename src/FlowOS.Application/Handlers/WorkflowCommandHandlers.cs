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
        Guid definitionId;

        if (request.WorkflowDefinitionId.HasValue)
        {
            definitionId = request.WorkflowDefinitionId.Value;
        }
        else if (!string.IsNullOrEmpty(request.WorkflowName))
        {
            if (request.Version.HasValue)
            {
                var def = await _context.WorkflowDefinitions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.Name == request.WorkflowName 
                        && w.Version == request.Version.Value 
                        && w.TenantId == request.TenantId, cancellationToken);
                
                if (def == null)
                {
                    throw new ArgumentException($"Workflow definition '{request.WorkflowName}' v{request.Version} not found.");
                }
                definitionId = def.Id;
            }
            else
            {
                // Resolve Latest Version
                var def = await _context.WorkflowDefinitions
                    .AsNoTracking()
                    .Where(w => w.Name == request.WorkflowName && w.TenantId == request.TenantId)
                    .OrderByDescending(w => w.Version)
                    .FirstOrDefaultAsync(cancellationToken);
                    
                if (def == null)
                {
                    throw new ArgumentException($"No definition found for workflow '{request.WorkflowName}'.");
                }
                definitionId = def.Id;
            }
        }
        else
        {
             throw new ArgumentException("Either WorkflowDefinitionId or WorkflowName must be provided.");
        }

        // Fix: If we resolved definitionId but request.Version was null, we need to know the actual version for the Instance.
        // The simplest way is to fetch the definition if we haven't already.
        // Or better: when resolving definitionId, also capture the version.
        
        int actualVersion = request.Version ?? 1;
        if (!request.Version.HasValue)
        {
             var def = await _context.WorkflowDefinitions
                 .AsNoTracking()
                 .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
             if (def != null) actualVersion = def.Version;
        }

        // 1. Create Instance
        var instance = new WorkflowInstance(
            request.TenantId,
            definitionId,
            actualVersion,
            request.InitialStepId,
            request.CorrelationId
        );

        // 2. Persist
        _context.WorkflowInstances.Add(instance);
        // await _context.SaveChangesAsync(cancellationToken); // Delay save to include auto-advance updates

        // 3. Auto-Advance from Start (if Default transition exists)
        // We need the full definition loaded.
        var fullDefinition = await _context.WorkflowDefinitions
             .AsNoTracking()
             .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);

        if (fullDefinition != null)
        {
            int autoAdvanceLimit = 5;
            while (autoAdvanceLimit > 0)
            {
                var currentStep = fullDefinition.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);
                if (currentStep != null && currentStep.NextSteps.ContainsKey("Default"))
                {
                    var defaultEvent = new StandardEvent(request.TenantId, "Default");
                    var autoResult = _engine.Advance(instance, fullDefinition, defaultEvent, new FlowOS.StateMachines.Models.ExecutionContext());
                    
                    if (!autoResult.Success) break;
                    autoAdvanceLimit--;
                }
                else
                {
                    break;
                }
            }
        }
        
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

        // 3. Create Event Wrapper
        var domainEvent = new StandardEvent(request.TenantId, request.EventType);
        
        // Auto-link to Workflow Instance if not explicitly correlated
        if (request.CorrelationId.HasValue)
        {
            domainEvent.SetCorrelationId(request.CorrelationId.Value);
        }
        else
        {
            // Default correlation to the target workflow instance
            domainEvent.SetCorrelationId(request.WorkflowInstanceId);
        }

        // Handle Payload (Simple serialization for now)
        if (request.Payload != null)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(request.Payload);
            domainEvent.AddMetadata("Payload", json);
        }

        // 4. Advance Workflow
        // Check for Auto-Advance loops (Default transitions)
        var result = _engine.Advance(instance, definition, domainEvent, new FlowOS.StateMachines.Models.ExecutionContext());

        if (result.Success)
        {
            // Persist the event that caused the transition
            _context.Events.Add(domainEvent);

            // If the new step has a "Default" transition, automatically advance
            // Simple loop protection: limit to 5 auto-advances
            int autoAdvanceLimit = 5;
            while (autoAdvanceLimit > 0)
            {
                var currentStep = definition.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);
                if (currentStep != null && currentStep.NextSteps.ContainsKey("Default"))
                {
                    // Create a dummy "Default" event
                    var defaultEvent = new StandardEvent(request.TenantId, "Default");
                    var autoResult = _engine.Advance(instance, definition, defaultEvent, new FlowOS.StateMachines.Models.ExecutionContext());
                    
                    if (!autoResult.Success) break; // Should not happen if config is correct
                    autoAdvanceLimit--;
                }
                else
                {
                    break;
                }
            }

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

    // GenericDomainEvent removed in favor of StandardEvent
}
