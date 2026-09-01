using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using FlowOS.Application.Commands;
using FlowOS.Domain.Entities;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Engine;
using System.Linq;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Interfaces;
using FlowOS.Security.Interfaces;
using FlowOS.Domain.ValueObjects;
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly WorkflowEngine _engine;
    private readonly IEventRegistry _eventRegistry;
    private readonly ICurrentUser _currentUser;
    private readonly ICapabilityService _capabilityService;

    public WorkflowCommandHandlers(
        IUnitOfWork unitOfWork, 
        IEventRegistry eventRegistry,
        ICurrentUser currentUser,
        ICapabilityService capabilityService,
        WorkflowEngine engine)
    {
        _unitOfWork = unitOfWork;
        _eventRegistry = eventRegistry;
        _engine = engine;
        _currentUser = currentUser;
        _capabilityService = capabilityService;
    }

    public async Task<Guid> Handle(StartWorkflowCommand request, CancellationToken cancellationToken)
    {
        WorkflowDefinition? fullDefinition = null;
        Guid definitionId;

        if (request.WorkflowDefinitionId.HasValue)
        {
            definitionId = request.WorkflowDefinitionId.Value;
            fullDefinition = await _unitOfWork.WorkflowDefinitions
                .GetByIdAsNoTrackingAsync(definitionId, cancellationToken);
        }
        else if (!string.IsNullOrEmpty(request.WorkflowName))
        {
            if (request.Version.HasValue)
            {
                fullDefinition = await _unitOfWork.WorkflowDefinitions
                    .GetPublishedByNameAndVersionAsync(request.WorkflowName, request.Version.Value, request.TenantId, cancellationToken);
                
                if (fullDefinition == null)
                {
                    throw new ArgumentException($"Workflow definition '{request.WorkflowName}' v{request.Version} not found or not published.");
                }
                definitionId = fullDefinition.Id;
            }
            else
            {
                // Resolve Latest Version
                fullDefinition = await _unitOfWork.WorkflowDefinitions
                    .GetLatestByNameAsync(request.WorkflowName, request.TenantId, cancellationToken);
                    
                if (fullDefinition == null)
                {
                    throw new ArgumentException($"No definition found for workflow '{request.WorkflowName}'. Ensure it is Published.");
                }
                
                if (fullDefinition.Status != WorkflowStatus.Published)
                {
                     throw new ArgumentException($"Workflow definition '{request.WorkflowName}' v{fullDefinition.Version} is in status '{fullDefinition.Status}', not Published.");
                }

                definitionId = fullDefinition.Id;
            }
        }
        else if (request.WorkflowClassId != Guid.Empty)
        {
             var wc = await _unitOfWork.WorkflowClasses
                 .GetByIdAsNoTrackingAsync(request.WorkflowClassId, cancellationToken);
             
             if (wc == null) throw new ArgumentException($"WorkflowClass {request.WorkflowClassId} not found.");
             
             int version = WorkflowVersion.Parse(wc.Version).RuntimeVersion;

             fullDefinition = await _unitOfWork.WorkflowDefinitions
                 .GetByNameAndVersionAsync(wc.Name, version, request.TenantId, cancellationToken);
                 
             if (fullDefinition == null) 
             {
                 var anyDef = await _unitOfWork.WorkflowDefinitions
                     .GetAnyByNameAsync(wc.Name, request.TenantId, cancellationToken);
                 
                 if (anyDef != null)
                    throw new ArgumentException($"Definition found for {wc.Name} but version mismatch (Class: {wc.Version} -> {version}, Def: {anyDef.Version}). Ensure Publish creates the definition.");
                 else
                 {
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
            startStep = fullDefinition.StartStepId;
        }

        if (!string.IsNullOrEmpty(request.InitialStepId))
        {
             if (fullDefinition != null && fullDefinition.Steps.Any(s => s.StepId == request.InitialStepId))
             {
                 startStep = request.InitialStepId;
             }
             else if (fullDefinition != null)
             {
                 throw new ArgumentException($"Requested initial step '{request.InitialStepId}' not found in definition.");
             }
        }
        
        if (fullDefinition != null && !fullDefinition.Steps.Any(s => s.StepId == startStep))
        {
             throw new ArgumentException($"Resolved Start Step '{startStep}' not found in definition '{fullDefinition.Name}'.");
        }
        // -----------------------------------------

        var instance = new WorkflowInstance(
            request.TenantId,
            definitionId,
            request.WorkflowClassId,
            actualVersion,
            startStep,
            request.CorrelationId
        );

        _unitOfWork.WorkflowInstances.Add(instance);

        if (fullDefinition != null)
        {
            RunAutoAdvance(instance, fullDefinition, request.TenantId, new FlowOS.StateMachines.Models.ExecutionContext());
        }
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return instance.Id;
    }

    public async Task<bool> Handle(PublishEventCommand request, CancellationToken cancellationToken)
    {
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

        var isRegistered = await _eventRegistry.ExistsAsync(request.EventType, request.TenantId);
        if (!isRegistered && request.EventType.StartsWith("EVT-", StringComparison.OrdinalIgnoreCase))
        {
             Console.WriteLine($"[Handler] Event '{request.EventType}' not registered for tenant {request.TenantId}");
             throw new ArgumentException($"Event '{request.EventType}' is not registered.");
        }

        var instance = await _unitOfWork.WorkflowInstances
            .GetByIdAsync(request.WorkflowInstanceId, request.TenantId, cancellationToken);

        if (instance == null) 
        {
            Console.WriteLine($"[Handler] Instance {request.WorkflowInstanceId} not found.");
            return false;
        }

        var definition = await _unitOfWork.WorkflowDefinitions
            .GetByIdAsync(instance.WorkflowDefinitionId, cancellationToken);
            
        if (definition == null) 
        {
            Console.WriteLine($"[Handler] Definition {instance.WorkflowDefinitionId} not found.");
            return false;
        }

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

        var context = new FlowOS.StateMachines.Models.ExecutionContext();
        
        if (request.Payload != null)
        {
            try 
            {
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
            _unitOfWork.Events.Add(domainEvent);
            RunAutoAdvance(instance, definition, request.TenantId, context);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        else 
        {
            Console.WriteLine($"[Handler] Advance failed. Current Step: {instance.CurrentStepId}, Event: {request.EventType}. Reason: {result.FailureReason}");
            throw new InvalidOperationException($"Workflow transition failed: {result.FailureReason}");
        }
    }

    public async Task<bool> Handle(CompleteTaskCommand request, CancellationToken cancellationToken)
    {
        var instance = await _unitOfWork.WorkflowInstances
            .GetByIdAsync(request.WorkflowInstanceId, request.TenantId, cancellationToken);

        if (instance == null) return false;

        var definition = await _unitOfWork.WorkflowDefinitions
            .GetByIdAsync(instance.WorkflowDefinitionId, cancellationToken);

        if (definition == null) return false;

        var domainEvent = new TaskCompleted(request.TenantId, request.TaskId, Guid.Empty);
        
        if (request.CorrelationId.HasValue)
        {
            domainEvent.SetCorrelationId(request.CorrelationId.Value);
        }

        var context = new FlowOS.StateMachines.Models.ExecutionContext();
        var result = _engine.Advance(instance, definition, domainEvent, context);

        if (result.Success)
        {
            _unitOfWork.Events.Add(domainEvent);
            RunAutoAdvance(instance, definition, request.TenantId, context);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    private void RunAutoAdvance(WorkflowInstance instance, WorkflowDefinition definition, Guid tenantId, FlowOS.StateMachines.Models.ExecutionContext context)
    {
        int autoAdvanceLimit = 5;
        while (autoAdvanceLimit > 0)
        {
            var currentStep = definition.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);
            if (currentStep != null && currentStep.NextSteps.ContainsKey("Default"))
            {
                var defaultEvent = new StandardEvent(tenantId, "Default");
                var result = _engine.Advance(instance, definition, defaultEvent, context);
                
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
