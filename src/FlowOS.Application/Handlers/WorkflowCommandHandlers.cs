using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FlowOS.Application.Commands;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Engine;
using System.Linq;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Interfaces;
using FlowOS.Security.Interfaces;
using FlowOS.Domain.Enums;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using FlowOS.Events.Models;
using System.Collections.Generic;

namespace FlowOS.Application.Handlers;

public class WorkflowCommandHandlers : 
    IRequestHandler<StartWorkflowCommand, Guid>,
    IRequestHandler<PublishEventCommand, bool>,
    IRequestHandler<CompleteTaskCommand, bool>
{
    private readonly FlowOSDbContext _context;
    private readonly WorkflowEngine _engine;
    private readonly IEventRegistry _eventRegistry;
    private readonly ICurrentUser _currentUser;
    private readonly ICapabilityService _capabilityService;

    public WorkflowCommandHandlers(
        FlowOSDbContext context, 
        IEventRegistry eventRegistry,
        ICurrentUser currentUser,
        ICapabilityService capabilityService)
    {
        _context = context;
        _eventRegistry = eventRegistry;
        _engine = new WorkflowEngine();
        _currentUser = currentUser;
        _capabilityService = capabilityService;
    }

    public async Task<Guid> Handle(StartWorkflowCommand request, CancellationToken cancellationToken)
    {
        WorkflowDefinition fullDefinition = null;
        Guid definitionId;

        if (request.WorkflowDefinitionId.HasValue)
        {
            definitionId = request.WorkflowDefinitionId.Value;
            fullDefinition = await _context.WorkflowDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        }
        else if (!string.IsNullOrEmpty(request.WorkflowName))
        {
            if (request.Version.HasValue)
            {
                fullDefinition = await _context.WorkflowDefinitions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.Name == request.WorkflowName 
                        && w.Version == request.Version.Value 
                        && w.TenantId == request.TenantId
                        && w.Status == WorkflowStatus.Published, cancellationToken);
                
                if (fullDefinition == null)
                {
                    throw new ArgumentException($"Workflow definition '{request.WorkflowName}' v{request.Version} not found or not published.");
                }
                definitionId = fullDefinition.Id;
            }
            else
            {
                // Resolve Latest Version
                fullDefinition = await _context.WorkflowDefinitions
                    .AsNoTracking()
                    .Where(w => w.Name == request.WorkflowName 
                        && w.TenantId == request.TenantId) // Removed Status check from here to allow diagnosis
                    .OrderByDescending(w => w.Version)
                    .FirstOrDefaultAsync(cancellationToken);
                    
                if (fullDefinition == null)
                {
                    throw new ArgumentException($"No definition found for workflow '{request.WorkflowName}'. Ensure it is Published.");
                }
                
                // Now check status if needed (Runtime definitions usually are considered published if they exist, but let's be safe)
                if (fullDefinition.Status != WorkflowStatus.Published)
                {
                     throw new ArgumentException($"Workflow definition '{request.WorkflowName}' v{fullDefinition.Version} is in status '{fullDefinition.Status}', not Published.");
                }

                definitionId = fullDefinition.Id;
            }
        }
        else if (request.WorkflowClassId != Guid.Empty)
        {
             var wc = await _context.WorkflowClasses
                 .AsNoTracking()
                 .FirstOrDefaultAsync(c => c.Id == request.WorkflowClassId, cancellationToken);
             
             if (wc == null) throw new ArgumentException($"WorkflowClass {request.WorkflowClassId} not found.");
             
             int version = 1;
             var versionStr = wc.Version;
             if (versionStr.StartsWith("v", StringComparison.OrdinalIgnoreCase)) versionStr = versionStr.Substring(1);
             var majorPart = versionStr.Split(new[] { '.', '-', '+' })[0];
             if (int.TryParse(majorPart, out var v)) version = v;

             fullDefinition = await _context.WorkflowDefinitions
                 .AsNoTracking()
                 .FirstOrDefaultAsync(d => d.Name == wc.Name && d.Version == version && d.TenantId == request.TenantId, cancellationToken);
                 
             if (fullDefinition == null) 
             {
                 // Fallback: Check if definition exists with any version for this name, to give better error
                 var anyDef = await _context.WorkflowDefinitions
                     .AsNoTracking()
                     .FirstOrDefaultAsync(d => d.Name == wc.Name && d.TenantId == request.TenantId, cancellationToken);
                 
                 if (anyDef != null)
                    throw new ArgumentException($"Definition found for {wc.Name} but version mismatch (Class: {wc.Version} -> {version}, Def: {anyDef.Version}). Ensure Publish creates the definition.");
                 else
                 {
                    // Fallback 2: Check if there's a Published WorkflowClass but no Definition (Compilation failed or skipped)
                    if (wc.Status == Domain.Enums.WorkflowClassStatus.Published)
                        throw new ArgumentException($"WorkflowClass '{wc.Name}' is Published but no Runtime Definition exists. Please re-publish to generate the definition.");
                    else
                        throw new ArgumentException($"No definition found for class '{wc.Name}'. The class is in status '{wc.Status}' - it must be Published to start.");
                 }
             }
             definitionId = fullDefinition.Id;
        }
        else
        {
             throw new ArgumentException("Either WorkflowDefinitionId, WorkflowClassId, or WorkflowName must be provided.");
        }

        int actualVersion = request.Version ?? 1;
        if (!request.Version.HasValue && fullDefinition != null)
        {
             actualVersion = fullDefinition.Version;
        }

        // --- FIX: Resolve Start Step Correctly ---
        string startStep = "Start";
        
        if (fullDefinition != null && !string.IsNullOrEmpty(fullDefinition.StartStepId))
        {
            // Default to Definition's Start Step
            startStep = fullDefinition.StartStepId;
        }

        if (!string.IsNullOrEmpty(request.InitialStepId))
        {
             // Override if user explicitly requested (and it exists)
             if (fullDefinition != null && fullDefinition.Steps.Any(s => s.StepId == request.InitialStepId))
             {
                 startStep = request.InitialStepId;
             }
             else if (fullDefinition != null)
             {
                 throw new ArgumentException($"Requested initial step '{request.InitialStepId}' not found in definition.");
             }
        }
        
        // Final Safety Check
        if (fullDefinition != null && !fullDefinition.Steps.Any(s => s.StepId == startStep))
        {
             throw new ArgumentException($"Resolved Start Step '{startStep}' not found in definition '{fullDefinition.Name}'.");
        }
        // -----------------------------------------

        // 1. Create Instance
        var instance = new WorkflowInstance(
            request.TenantId,
            definitionId,
            request.WorkflowClassId, // Use passed WorkflowClassId (might be Guid.Empty if not provided in old DTO)
            actualVersion,
            startStep,
            request.CorrelationId
        );

        // 2. Persist
        _context.WorkflowInstances.Add(instance);

        // 3. Auto-Advance from Start (if Default transition exists)
        if (fullDefinition != null)
        {
            RunAutoAdvance(instance, fullDefinition, request.TenantId);
        }
        
        await _context.SaveChangesAsync(cancellationToken);

        return instance.Id;
    }

    public async Task<bool> Handle(PublishEventCommand request, CancellationToken cancellationToken)
    {
        // 0. Dynamic Permission Check
        var requiredCapability = $"event.publish.{request.EventType}";
        var userRoles = _currentUser.Roles ?? new List<string>();
        
        var capabilities = await _capabilityService.GetCapabilitiesAsync(request.TenantId, userRoles);
        
        bool hasSpecific = capabilities.Contains(requiredCapability);
        bool hasRoot = capabilities.Contains("event.publish");
        
        if (!hasSpecific && !hasRoot)
        {
             Console.WriteLine($"[WorkflowHandler] Access Denied. User {_currentUser.Id} (Roles: {string.Join(",", userRoles)}) lacks {requiredCapability}");
             throw new FlowOS.Application.Common.Exceptions.PolicyViolationException("EventPermission", $"User lacks permission to publish '{request.EventType}'. Required: {requiredCapability}");
        }

        // 0.1 Validate Event ID
        var isRegistered = await _eventRegistry.ExistsAsync(request.EventType, request.TenantId);
        if (!isRegistered && request.EventType.StartsWith("EVT-"))
        {
             Console.WriteLine($"[Handler] Event '{request.EventType}' not registered for tenant {request.TenantId}");
             return false;
        }

        // 1. Load Workflow Instance
        var instance = await _context.WorkflowInstances
            .FirstOrDefaultAsync(w => w.Id == request.WorkflowInstanceId && w.TenantId == request.TenantId, cancellationToken);

        if (instance == null) 
        {
            Console.WriteLine($"[Handler] Instance {request.WorkflowInstanceId} not found.");
            return false;
        }

        // 2. Load Definition
        var definition = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(d => d.Id == instance.WorkflowDefinitionId, cancellationToken);
            
        if (definition == null) 
        {
            Console.WriteLine($"[Handler] Definition {instance.WorkflowDefinitionId} not found.");
            return false;
        }

        // 3. Create Event Wrapper
        var domainEvent = new StandardEvent(request.TenantId, request.EventType);
        
        if (request.CorrelationId.HasValue)
        {
            domainEvent.SetCorrelationId(request.CorrelationId.Value);
        }
        else
        {
            domainEvent.SetCorrelationId(request.WorkflowInstanceId);
        }

        if (request.Payload != null)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(request.Payload);
            domainEvent.AddMetadata("Payload", json);
        }

        // 4. Advance Workflow
        var context = new FlowOS.StateMachines.Models.ExecutionContext();
        
        if (request.Payload != null)
        {
            try 
            {
                // Try to convert payload to Dictionary<string, object> for the Engine
                var jsonString = System.Text.Json.JsonSerializer.Serialize(request.Payload);
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
                if (dict != null)
                {
                    context.Payload = dict;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Handler] Failed to parse payload for ExecutionContext: {ex.Message}");
            }
        }

        var result = _engine.Advance(instance, definition, domainEvent, context);

        if (result.Success)
        {
            // Persist the event that caused the transition
            _context.Events.Add(domainEvent);

            // Check for Auto-Advance loops (Default transitions)
            RunAutoAdvance(instance, definition, request.TenantId);

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
        var domainEvent = new TaskCompleted(request.TenantId, request.TaskId, Guid.Empty);
        
        if (request.CorrelationId.HasValue)
        {
            domainEvent.SetCorrelationId(request.CorrelationId.Value);
        }

        // 4. Advance Workflow via Engine
        var result = _engine.Advance(instance, definition, domainEvent, new FlowOS.StateMachines.Models.ExecutionContext());

        if (result.Success)
        {
            // 5. Persist Event & State
            _context.Events.Add(domainEvent);

            // Check for Auto-Advance loops (Default transitions)
            RunAutoAdvance(instance, definition, request.TenantId);

            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    private void RunAutoAdvance(WorkflowInstance instance, WorkflowDefinition definition, Guid tenantId)
    {
        int autoAdvanceLimit = 5;
        while (autoAdvanceLimit > 0)
        {
            var currentStep = definition.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);
            if (currentStep != null && currentStep.NextSteps.ContainsKey("Default"))
            {
                var defaultEvent = new StandardEvent(tenantId, "Default");
                var result = _engine.Advance(instance, definition, defaultEvent, new FlowOS.StateMachines.Models.ExecutionContext());
                
                if (!result.Success) break;
                autoAdvanceLimit--;
            }
            else
            {
                break;
            }
        }
    }
}
