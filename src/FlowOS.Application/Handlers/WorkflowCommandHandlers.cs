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
using FlowOS.Domain.Enums;
using FlowOS.Domain.ValueObjects;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using FlowOS.Events.Models;
using System.Collections.Generic;
using FlowOS.Core.Common.Interfaces;

namespace FlowOS.Application.Handlers;

public class WorkflowCommandHandlers : 
    IRequestHandler<StartWorkflowCommand, Guid>,
    IRequestHandler<PublishEventCommand, bool>,
    IRequestHandler<CompleteTaskCommand, bool>
{
    private const string StartWorkflowOperation = "start_workflow";
    private const string PublishEventOperation = "publish_event";
    private const string CompleteTaskOperation = "complete_task";

    private readonly IUnitOfWork _unitOfWork;
    private readonly WorkflowEngine _engine;
    private readonly IEventRegistry _eventRegistry;
    private readonly ICurrentUser _currentUser;
    private readonly ICapabilityService _capabilityService;
    private readonly FlowOS.Application.Common.Interfaces.IWorkflowTimerService? _timerService;
    private readonly FlowOS.Application.Common.Interfaces.IWorkflowActionDispatcher? _actionDispatcher;
    private readonly IIdempotencyService? _idempotencyService;
    private readonly IPluginBindingRegistryService? _pluginBindingRegistry;

    public WorkflowCommandHandlers(
        IUnitOfWork unitOfWork, 
        IEventRegistry eventRegistry,
        ICurrentUser currentUser,
        ICapabilityService capabilityService,
        WorkflowEngine engine,
        FlowOS.Application.Common.Interfaces.IWorkflowTimerService? timerService = null,
        FlowOS.Application.Common.Interfaces.IWorkflowActionDispatcher? actionDispatcher = null,
        IIdempotencyService? idempotencyService = null,
        IPluginBindingRegistryService? pluginBindingRegistry = null)
    {
        _unitOfWork = unitOfWork;
        _eventRegistry = eventRegistry;
        _engine = engine;
        _currentUser = currentUser;
        _capabilityService = capabilityService;
        _timerService = timerService;
        _actionDispatcher = actionDispatcher;
        _idempotencyService = idempotencyService;
        _pluginBindingRegistry = pluginBindingRegistry;
    }

    public async Task<Guid> Handle(StartWorkflowCommand request, CancellationToken cancellationToken)
    {
        if (_idempotencyService != null && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var cached = await _idempotencyService.TryGetCompletedResultAsync<Guid>(
                request.TenantId, StartWorkflowOperation, request.IdempotencyKey, cancellationToken);
            if (cached.Found && cached.Result != Guid.Empty)
            {
                return cached.Result;
            }

            var started = await _idempotencyService.TryBeginAsync(
                request.TenantId, StartWorkflowOperation, request.IdempotencyKey, cancellationToken);
            if (!started)
            {
                throw new InvalidOperationException(
                    $"Duplicate idempotent request for '{StartWorkflowOperation}' is already pending or completed.");
            }
        }

        try
        {
        WorkflowDefinition? fullDefinition = null;
        Guid definitionId;

        if (request.WorkflowDefinitionId.HasValue)
        {
            definitionId = request.WorkflowDefinitionId.Value;
            fullDefinition = await _unitOfWork.WorkflowDefinitions
                .GetByIdAsNoTrackingAsync(definitionId, cancellationToken);

            if (fullDefinition == null || fullDefinition.TenantId != request.TenantId)
            {
                throw new ArgumentException($"Workflow definition '{definitionId}' not found.");
            }
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
             
             if (wc == null || (wc.TenantId != request.TenantId && wc.Scope != Domain.Enums.WorkflowClassScope.Public))
                 throw new ArgumentException($"WorkflowClass '{request.WorkflowClassId}' not found.");
             
             int version = WorkflowVersion.Parse(wc.Version).RuntimeVersion;

              fullDefinition = await _unitOfWork.WorkflowDefinitions
                  .GetByNameAndVersionAsync(wc.Name, version, request.TenantId, cancellationToken);
                  
              if (fullDefinition == null && wc.Scope == Domain.Enums.WorkflowClassScope.Public)
              {
                  fullDefinition = await _unitOfWork.WorkflowDefinitions
                      .GetByNameAndVersionAsync(wc.Name, version, wc.TenantId, cancellationToken);
              }

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

        var startEvent = new StandardEvent(request.TenantId, "WorkflowStarted");
        startEvent.SetCorrelationId(instance.Id);
        startEvent.AddMetadata("WorkflowName", fullDefinition?.Name ?? request.WorkflowName ?? "Workflow");
        startEvent.AddMetadata("Version", instance.WorkflowVersion.ToString());
        startEvent.AddMetadata("StartStep", startStep);
        startEvent.AddMetadata("InitialState", instance.CurrentState ?? "Draft");
        if (!string.IsNullOrEmpty(_currentUser.Id))
        {
            startEvent.AddMetadata("ActorId", _currentUser.Id);
        }
        _unitOfWork.Events.Add(startEvent);

        if (fullDefinition != null)
        {
            var initialEnteredStepIds = new List<string> { instance.CurrentStepId };

            if (_actionDispatcher != null && !string.IsNullOrEmpty(instance.CurrentStepId))
            {
                var initialStep = fullDefinition.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);
                if (initialStep?.OnEntry != null && initialStep.OnEntry.Count > 0)
                {
                    await _actionDispatcher.QueueActionsAsync(
                        request.TenantId,
                        instance.Id,
                        initialStep.StepId,
                        "OnEntry",
                        initialStep.OnEntry,
                        null,
                        cancellationToken);
                }
            }

            await StartSubWorkflowChildrenForEnteredStepsAsync(
                instance,
                fullDefinition,
                initialEnteredStepIds,
                null,
                cancellationToken);

            var autoAdvanceContext = new FlowOS.StateMachines.Models.ExecutionContext();
            if (request.Payload != null)
            {
                autoAdvanceContext.Payload = ToPayloadDictionary(request.Payload) ?? new Dictionary<string, object>();
            }
            await EnrichExecutionContextWithPluginBindingsAsync(autoAdvanceContext, request.TenantId, cancellationToken);
            RunAutoAdvance(instance, fullDefinition, request.TenantId, autoAdvanceContext);
            var autoAdvancedEnteredStepIds = (instance.ActiveStepIds != null && instance.ActiveStepIds.Count > 0)
                ? instance.ActiveStepIds.ToList()
                : (string.IsNullOrEmpty(instance.CurrentStepId) ? new List<string>() : new List<string> { instance.CurrentStepId });
            await StartSubWorkflowChildrenForEnteredStepsAsync(
                instance,
                fullDefinition,
                autoAdvancedEnteredStepIds,
                autoAdvanceContext.Payload,
                cancellationToken);
            await CheckAndScheduleTimerAsync(instance, fullDefinition, request.TenantId, autoAdvanceContext.Payload, cancellationToken);
        }
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_idempotencyService != null && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            await _idempotencyService.CompleteAsync(
                request.TenantId, StartWorkflowOperation, request.IdempotencyKey, instance.Id, cancellationToken);
        }

        return instance.Id;
        }
        catch (Exception ex)
        {
            if (_idempotencyService != null && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                await _idempotencyService.FailAsync(
                    request.TenantId, StartWorkflowOperation, request.IdempotencyKey, ex.Message, cancellationToken);
            }
            throw;
        }
    }

    public async Task<bool> Handle(PublishEventCommand request, CancellationToken cancellationToken)
    {
        if (_idempotencyService != null && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var cached = await _idempotencyService.TryGetCompletedResultAsync<bool>(
                request.TenantId, PublishEventOperation, request.IdempotencyKey, cancellationToken);
            if (cached.Found)
            {
                return cached.Result == true;
            }

            var started = await _idempotencyService.TryBeginAsync(
                request.TenantId, PublishEventOperation, request.IdempotencyKey, cancellationToken);
            if (!started)
            {
                throw new InvalidOperationException(
                    $"Duplicate idempotent request for '{PublishEventOperation}' is already pending or completed.");
            }
        }

        try
        {
        var userRoles = _currentUser.Roles ?? new List<string>();
        if (userRoles.Any() || !string.IsNullOrEmpty(_currentUser.Id))
        {
            var requiredCapability = $"event.publish.{request.EventType}";
            var capabilities = await _capabilityService.GetCapabilitiesAsync(request.TenantId, userRoles);
            
            bool hasSpecific = capabilities.Contains(requiredCapability);
            bool hasRoot = capabilities.Contains("event.publish");
            
            if (!hasSpecific && !hasRoot)
            {
                 Console.WriteLine($"[WorkflowHandler] Access Denied. User {_currentUser.Id} (Roles: {string.Join(",", userRoles)}) lacks {requiredCapability}");
                 throw new FlowOS.Application.Common.Exceptions.PolicyViolationException("EventPermission", $"User lacks permission to publish '{request.EventType}'. Required: {requiredCapability}");
            }
        }

        Console.WriteLine($"[Handler] Handling PublishEventCommand: Event={request.EventType}, WorkflowInstanceId={request.WorkflowInstanceId}, Tenant={request.TenantId}");
        
        bool isRegistered = await _eventRegistry.ExistsAsync(request.EventType, request.TenantId);
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
        await EnrichExecutionContextWithPluginBindingsAsync(context, request.TenantId, cancellationToken);

        var smDef = await ResolveStateMachineDefinitionAsync(instance.WorkflowClassId, request.TenantId, definition.Name, cancellationToken);
        var currentEntityState = instance.CurrentState ?? instance.CurrentStepId;

        var previousStepId = instance.CurrentStepId;
        var previousState = instance.CurrentState ?? instance.CurrentStepId;
        var result = _engine.Advance(instance, definition, domainEvent, context, smDef, currentEntityState);

        if (result.Success)
        {
            if (_timerService != null && !string.IsNullOrEmpty(previousStepId))
            {
                await _timerService.CancelTimerAsync(instance.Id, previousStepId, cancellationToken);
            }

            // Trigger OnExit actions of departed step
            if (_actionDispatcher != null && !string.IsNullOrEmpty(previousStepId))
            {
                var departedStep = definition.Steps.FirstOrDefault(s => s.StepId == previousStepId);
                if (departedStep?.OnExit != null && departedStep.OnExit.Count > 0)
                {
                    await _actionDispatcher.QueueActionsAsync(
                        request.TenantId,
                        instance.Id,
                        previousStepId,
                        "OnExit",
                        departedStep.OnExit,
                        context.Payload,
                        cancellationToken);
                }
            }

            // Trigger OnEntry actions of new step(s)
            var enteredStepIds = (instance.ActiveStepIds != null && instance.ActiveStepIds.Count > 0)
                ? instance.ActiveStepIds.ToList()
                : (string.IsNullOrEmpty(instance.CurrentStepId) ? new List<string>() : new List<string> { instance.CurrentStepId });

            if (_actionDispatcher != null)
            {
                foreach (var stepId in enteredStepIds)
                {
                    var enteredStep = definition.Steps.FirstOrDefault(s => s.StepId == stepId);
                    if (enteredStep?.OnEntry != null && enteredStep.OnEntry.Count > 0)
                    {
                        await _actionDispatcher.QueueActionsAsync(
                            request.TenantId,
                            instance.Id,
                            stepId,
                            "OnEntry",
                            enteredStep.OnEntry,
                            context.Payload,
                            cancellationToken);
                    }
                }
            }

            await StartSubWorkflowChildrenForEnteredStepsAsync(
                instance,
                definition,
                enteredStepIds,
                context.Payload,
                cancellationToken);

            domainEvent.AddMetadata("FromStep", previousStepId ?? "Start");
            domainEvent.AddMetadata("ToStep", instance.CurrentStepId);
            domainEvent.AddMetadata("FromState", previousState ?? "Draft");
            domainEvent.AddMetadata("ToState", instance.CurrentState ?? "Draft");
            if (!string.IsNullOrEmpty(_currentUser.Id))
            {
                domainEvent.AddMetadata("ActorId", _currentUser.Id);
            }

            _unitOfWork.Events.Add(domainEvent);

            if (instance.Status == FlowOS.Workflows.Enums.WorkflowInstanceStatus.Completed)
            {
                var completionEvent = new StandardEvent(request.TenantId, "WorkflowCompleted");
                completionEvent.SetCorrelationId(instance.Id);
                completionEvent.AddMetadata("CompletedStep", instance.CurrentStepId);
                completionEvent.AddMetadata("FinalState", instance.CurrentState ?? "Completed");
                _unitOfWork.Events.Add(completionEvent);

                await TryResumeParentWorkflowOnChildCompletionAsync(instance, cancellationToken);
            }

            RunAutoAdvance(instance, definition, request.TenantId, context, smDef);
            var autoAdvancedEnteredStepIds = (instance.ActiveStepIds != null && instance.ActiveStepIds.Count > 0)
                ? instance.ActiveStepIds.ToList()
                : (string.IsNullOrEmpty(instance.CurrentStepId) ? new List<string>() : new List<string> { instance.CurrentStepId });
            await StartSubWorkflowChildrenForEnteredStepsAsync(
                instance,
                definition,
                autoAdvancedEnteredStepIds,
                context.Payload,
                cancellationToken);
            await CheckAndScheduleTimerAsync(instance, definition, request.TenantId, context.Payload, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (_idempotencyService != null && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                await _idempotencyService.CompleteAsync(
                    request.TenantId, PublishEventOperation, request.IdempotencyKey, true, cancellationToken);
            }
            return true;
        }
        else 
        {
            Console.WriteLine($"[Handler] Advance failed. Current Step: {instance.CurrentStepId}, Event: {request.EventType}. Reason: {result.FailureReason}");

            if (_actionDispatcher != null && !string.IsNullOrEmpty(instance.CurrentStepId))
            {
                var failedStep = definition.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);
                if (failedStep?.OnFailure != null && failedStep.OnFailure.Count > 0)
                {
                    var failPayload = new Dictionary<string, object>(context.Payload ?? new Dictionary<string, object>())
                    {
                        ["FailureReason"] = result.FailureReason ?? "Transition failed",
                        ["EventType"] = request.EventType
                    };

                    await _actionDispatcher.QueueActionsAsync(
                        request.TenantId,
                        instance.Id,
                        instance.CurrentStepId,
                        "OnFailure",
                        failedStep.OnFailure,
                        failPayload,
                        cancellationToken);

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }

            throw new InvalidOperationException($"Workflow transition failed: {result.FailureReason}");
        }
        }
        catch (Exception ex)
        {
            if (_idempotencyService != null && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                await _idempotencyService.FailAsync(
                    request.TenantId, PublishEventOperation, request.IdempotencyKey, ex.Message, cancellationToken);
            }
            throw;
        }
    }

    public async Task<bool> Handle(CompleteTaskCommand request, CancellationToken cancellationToken)
    {
        if (_idempotencyService != null && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var cached = await _idempotencyService.TryGetCompletedResultAsync<bool>(
                request.TenantId, CompleteTaskOperation, request.IdempotencyKey, cancellationToken);
            if (cached.Found)
            {
                return cached.Result == true;
            }

            var started = await _idempotencyService.TryBeginAsync(
                request.TenantId, CompleteTaskOperation, request.IdempotencyKey, cancellationToken);
            if (!started)
            {
                throw new InvalidOperationException(
                    $"Duplicate idempotent request for '{CompleteTaskOperation}' is already pending or completed.");
            }
        }

        try
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
        else
        {
            domainEvent.SetCorrelationId(request.WorkflowInstanceId);
        }

        var context = new FlowOS.StateMachines.Models.ExecutionContext();
        await EnrichExecutionContextWithPluginBindingsAsync(context, request.TenantId, cancellationToken);
        var smDef = await ResolveStateMachineDefinitionAsync(instance.WorkflowClassId, request.TenantId, definition.Name, cancellationToken);
        var currentEntityState = instance.CurrentState ?? instance.CurrentStepId;

        var previousStepId = instance.CurrentStepId;
        var previousState = instance.CurrentState ?? instance.CurrentStepId;
        var result = _engine.Advance(instance, definition, domainEvent, context, smDef, currentEntityState);

        if (result.Success)
        {
            if (_timerService != null && !string.IsNullOrEmpty(previousStepId))
            {
                await _timerService.CancelTimerAsync(instance.Id, previousStepId, cancellationToken);
            }

            // Trigger OnExit actions of departed step
            if (_actionDispatcher != null && !string.IsNullOrEmpty(previousStepId))
            {
                var departedStep = definition.Steps.FirstOrDefault(s => s.StepId == previousStepId);
                if (departedStep?.OnExit != null && departedStep.OnExit.Count > 0)
                {
                    await _actionDispatcher.QueueActionsAsync(
                        request.TenantId,
                        instance.Id,
                        previousStepId,
                        "OnExit",
                        departedStep.OnExit,
                        null,
                        cancellationToken);
                }
            }

            // Trigger OnEntry actions of new step(s)
            var enteredStepIds = (instance.ActiveStepIds != null && instance.ActiveStepIds.Count > 0)
                ? instance.ActiveStepIds.ToList()
                : (string.IsNullOrEmpty(instance.CurrentStepId) ? new List<string>() : new List<string> { instance.CurrentStepId });

            if (_actionDispatcher != null)
            {
                foreach (var stepId in enteredStepIds)
                {
                    var enteredStep = definition.Steps.FirstOrDefault(s => s.StepId == stepId);
                    if (enteredStep?.OnEntry != null && enteredStep.OnEntry.Count > 0)
                    {
                        await _actionDispatcher.QueueActionsAsync(
                            request.TenantId,
                            instance.Id,
                            stepId,
                            "OnEntry",
                            enteredStep.OnEntry,
                            null,
                            cancellationToken);
                    }
                }
            }

            await StartSubWorkflowChildrenForEnteredStepsAsync(
                instance,
                definition,
                enteredStepIds,
                null,
                cancellationToken);

            domainEvent.AddMetadata("FromStep", previousStepId ?? "Start");
            domainEvent.AddMetadata("ToStep", instance.CurrentStepId);
            domainEvent.AddMetadata("FromState", previousState ?? "Draft");
            domainEvent.AddMetadata("ToState", instance.CurrentState ?? "Draft");
            if (!string.IsNullOrEmpty(_currentUser.Id))
            {
                domainEvent.AddMetadata("ActorId", _currentUser.Id);
            }

            _unitOfWork.Events.Add(domainEvent);

            if (instance.Status == FlowOS.Workflows.Enums.WorkflowInstanceStatus.Completed)
            {
                var completionEvent = new StandardEvent(request.TenantId, "WorkflowCompleted");
                completionEvent.SetCorrelationId(instance.Id);
                completionEvent.AddMetadata("CompletedStep", instance.CurrentStepId);
                completionEvent.AddMetadata("FinalState", instance.CurrentState ?? "Completed");
                _unitOfWork.Events.Add(completionEvent);

                await TryResumeParentWorkflowOnChildCompletionAsync(instance, cancellationToken);
            }

            RunAutoAdvance(instance, definition, request.TenantId, context, smDef);
            var autoAdvancedEnteredStepIds = (instance.ActiveStepIds != null && instance.ActiveStepIds.Count > 0)
                ? instance.ActiveStepIds.ToList()
                : (string.IsNullOrEmpty(instance.CurrentStepId) ? new List<string>() : new List<string> { instance.CurrentStepId });
            await StartSubWorkflowChildrenForEnteredStepsAsync(
                instance,
                definition,
                autoAdvancedEnteredStepIds,
                context.Payload,
                cancellationToken);
            await CheckAndScheduleTimerAsync(instance, definition, request.TenantId, context.Payload, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (_idempotencyService != null && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                await _idempotencyService.CompleteAsync(
                    request.TenantId, CompleteTaskOperation, request.IdempotencyKey, true, cancellationToken);
            }
            return true;
        }
        else
        {
            if (_actionDispatcher != null && !string.IsNullOrEmpty(instance.CurrentStepId))
            {
                var failedStep = definition.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);
                if (failedStep?.OnFailure != null && failedStep.OnFailure.Count > 0)
                {
                    var failPayload = new Dictionary<string, object>
                    {
                        ["FailureReason"] = result.FailureReason ?? "Task advance failed",
                        ["TaskId"] = request.TaskId.ToString()
                    };

                    await _actionDispatcher.QueueActionsAsync(
                        request.TenantId,
                        instance.Id,
                        instance.CurrentStepId,
                        "OnFailure",
                        failedStep.OnFailure,
                        failPayload,
                        cancellationToken);

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }
        }

        return false;
        }
        catch (Exception ex)
        {
            if (_idempotencyService != null && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                await _idempotencyService.FailAsync(
                    request.TenantId, CompleteTaskOperation, request.IdempotencyKey, ex.Message, cancellationToken);
            }
            throw;
        }
    }

    private async Task<FlowOS.Domain.Entities.StateMachineDefinition?> ResolveStateMachineDefinitionAsync(
        Guid workflowClassId,
        Guid tenantId,
        string workflowName,
        CancellationToken cancellationToken)
    {
        if (workflowClassId != Guid.Empty)
        {
            var wc = await _unitOfWork.WorkflowClasses.GetByIdAsNoTrackingAsync(workflowClassId, cancellationToken);
            if (wc?.Definition.StateMachine != null)
            {
                var smBp = wc.Definition.StateMachine;
                var smDef = new FlowOS.Domain.Entities.StateMachineDefinition(
                    tenantId,
                    smBp.EntityType ?? wc.Name,
                    smBp.InitialState ?? "Start"
                );
                foreach (var state in smBp.States)
                {
                    if (state != smDef.InitialState) smDef.AddState(state);
                }
                foreach (var tr in smBp.Transitions)
                {
                    smDef.AddTransition(new FlowOS.Domain.ValueObjects.StateTransition
                    {
                        FromState = tr.FromState,
                        ToState = tr.ToState,
                        EventId = tr.EventId
                    });
                }
                return smDef;
            }
        }
        else if (!string.IsNullOrEmpty(workflowName))
        {
            var smDef = await _unitOfWork.StateMachines.GetByEntityTypeAndTenantAsync(workflowName, tenantId, cancellationToken);
            if (smDef != null) return smDef;
        }

        return null;
    }

    private void RunAutoAdvance(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        Guid tenantId,
        FlowOS.StateMachines.Models.ExecutionContext context,
        FlowOS.Domain.Entities.StateMachineDefinition? smDef = null)
    {
        int autoAdvanceLimit = 10;
        while (autoAdvanceLimit > 0)
        {
            var activeSteps = (instance.ActiveStepIds != null && instance.ActiveStepIds.Count > 0)
                ? instance.ActiveStepIds.ToList()
                : (string.IsNullOrEmpty(instance.CurrentStepId) ? new List<string>() : new List<string> { instance.CurrentStepId });

            bool advancedAny = false;
            foreach (var stepId in activeSteps)
            {
                var step = definition.Steps.FirstOrDefault(s => s.StepId == stepId);
                if (step != null && step.NextSteps.ContainsKey("Default") && 
                    step.StepType != FlowOS.Workflows.Enums.WorkflowStepType.HumanTask && 
                    step.StepType != FlowOS.Workflows.Enums.WorkflowStepType.Timer &&
                    step.StepType != FlowOS.Workflows.Enums.WorkflowStepType.SubWorkflow)
                {
                    var defaultEvent = new StandardEvent(tenantId, "Default");
                    var currentEntityState = instance.CurrentState ?? instance.CurrentStepId;
                    var result = _engine.Advance(instance, definition, defaultEvent, context, smDef, currentEntityState);
                    if (result.Success)
                    {
                        advancedAny = true;
                        break;
                    }
                }
            }

            if (!advancedAny) break;
            autoAdvanceLimit--;
        }
    }

    private async Task CheckAndScheduleTimerAsync(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        Guid tenantId,
        Dictionary<string, object>? payload = null,
        CancellationToken cancellationToken = default)
    {
        if (_timerService == null) return;
        if (instance.Status != FlowOS.Workflows.Enums.WorkflowInstanceStatus.Waiting &&
            instance.Status != FlowOS.Workflows.Enums.WorkflowInstanceStatus.Running)
        {
            return;
        }

        var activeStepIds = (instance.ActiveStepIds != null && instance.ActiveStepIds.Count > 0)
            ? instance.ActiveStepIds.ToList()
            : (string.IsNullOrEmpty(instance.CurrentStepId) ? new List<string>() : new List<string> { instance.CurrentStepId });

        foreach (var stepId in activeStepIds)
        {
            var currentStep = definition.Steps.FirstOrDefault(s => s.StepId == stepId);
            if (currentStep == null) continue;

            // 1. Standalone Timer Step (supports static duration, explicit scheduledAt, and relative pre/post-event timers)
            if (currentStep.StepType == FlowOS.Workflows.Enums.WorkflowStepType.Timer)
            {
                var triggerEvent = currentStep.NextSteps.Keys.FirstOrDefault() ?? "Default";
                var (dueTimeUtc, duration) = ResolveTimerSchedule(currentStep.Conditions, payload);

                if (dueTimeUtc.HasValue)
                {
                    await _timerService.ScheduleTimerAtAsync(tenantId, instance.Id, currentStep.StepId, dueTimeUtc.Value, triggerEvent, cancellationToken);
                }
                else
                {
                    await _timerService.ScheduleTimerAsync(tenantId, instance.Id, currentStep.StepId, duration, triggerEvent, cancellationToken);
                }
            }
            // 2. Declarative Step SLA & Boundary Timer (with multi-tier reminders)
            else if (currentStep.Sla != null)
            {
                var triggerEvent = currentStep.Sla.TimeoutEvent;
                var slaDuration = ParseDuration(currentStep.Sla.Duration);
                var slaDueTimeUtc = DateTime.UtcNow.Add(slaDuration);

                await _timerService.ScheduleTimerAtAsync(tenantId, instance.Id, currentStep.StepId, slaDueTimeUtc, triggerEvent, cancellationToken);

                // Schedule SLA Reminders if defined
                if (currentStep.Sla.Reminders != null && currentStep.Sla.Reminders.Count > 0)
                {
                    foreach (var reminder in currentStep.Sla.Reminders)
                    {
                        if (string.IsNullOrWhiteSpace(reminder.Duration) || string.IsNullOrWhiteSpace(reminder.TriggerEvent))
                            continue;

                        var remDurStr = reminder.Duration.Trim();
                        DateTime remDueTimeUtc;

                        if (remDurStr.StartsWith("-"))
                        {
                            // Negative offset relative to SLA timeout (e.g. "-2h" before SLA timeout)
                            var offset = ParseDuration(remDurStr);
                            remDueTimeUtc = slaDueTimeUtc.Add(offset);
                        }
                        else
                        {
                            // Positive offset relative to step start (e.g. "24h" into the task)
                            var offset = ParseDuration(remDurStr);
                            remDueTimeUtc = DateTime.UtcNow.Add(offset);
                        }

                        if (remDueTimeUtc <= DateTime.UtcNow)
                        {
                            remDueTimeUtc = DateTime.UtcNow.AddSeconds(1);
                        }

                        await _timerService.ScheduleTimerAtAsync(
                            tenantId,
                            instance.Id,
                            currentStep.StepId,
                            remDueTimeUtc,
                            reminder.TriggerEvent,
                            cancellationToken);
                    }
                }
            }
        }
    }

    private (DateTime? DueTimeUtc, TimeSpan Duration) ResolveTimerSchedule(
        Dictionary<string, string>? conditions,
        Dictionary<string, object>? payload)
    {
        if (conditions == null || conditions.Count == 0)
            return (null, TimeSpan.FromSeconds(5));

        // 1. Explicit scheduled timestamp in conditions (e.g. "scheduledAt": "2026-10-01T15:00:00Z")
        string? scheduledAtStr = GetConditionValue(conditions, "scheduledAt", "scheduledTime", "dueTime");
        if (!string.IsNullOrWhiteSpace(scheduledAtStr) &&
            DateTime.TryParse(scheduledAtStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var explicitDt))
        {
            var dueUtc = explicitDt <= DateTime.UtcNow ? DateTime.UtcNow.AddSeconds(1) : explicitDt;
            return (dueUtc, dueUtc - DateTime.UtcNow);
        }

        // 2. Relative event-based timer (pre-event lead-time or post-event delay)
        string? targetProp = GetConditionValue(conditions, "targetTimestampProperty", "targetTimestamp", "referenceDate", "targetDate", "eventDate");
        if (!string.IsNullOrWhiteSpace(targetProp) && payload != null)
        {
            var key = payload.Keys.FirstOrDefault(k => string.Equals(k, targetProp, StringComparison.OrdinalIgnoreCase));
            if (key != null && payload[key] != null)
            {
                var val = payload[key];
                DateTime targetDt = default;
                bool parsed = false;

                if (val is DateTime dt)
                {
                    targetDt = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
                    parsed = true;
                }
                else if (val is string str && DateTime.TryParse(str, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var sDt))
                {
                    targetDt = sDt;
                    parsed = true;
                }
                else if (val is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.String &&
                         DateTime.TryParse(elem.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var eDt))
                {
                    targetDt = eDt;
                    parsed = true;
                }

                if (parsed)
                {
                    string? leadTimeStr = GetConditionValue(conditions, "leadTime", "offset", "delay");
                    var offset = !string.IsNullOrWhiteSpace(leadTimeStr) ? ParseDuration(leadTimeStr) : TimeSpan.Zero;
                    var scheduledUtc = targetDt.Add(offset);
                    var dueUtc = scheduledUtc <= DateTime.UtcNow ? DateTime.UtcNow.AddSeconds(1) : scheduledUtc;
                    return (dueUtc, dueUtc - DateTime.UtcNow);
                }
            }
        }

        // 3. Static duration string (e.g. "duration": "24h", "duration": "5s")
        string? durStr = GetConditionValue(conditions, "duration");
        var duration = ParseDuration(durStr);
        return (null, duration);
    }

    private static string? GetConditionValue(Dictionary<string, string> conditions, params string[] keys)
    {
        foreach (var key in keys)
        {
            var match = conditions.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (match != null && conditions.TryGetValue(match, out var val) && !string.IsNullOrWhiteSpace(val))
            {
                return val;
            }
        }
        return null;
    }

    private static Dictionary<string, object>? ToPayloadDictionary(object? payload)
    {
        if (payload == null) return null;
        if (payload is Dictionary<string, object> dict) return dict;
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task EnrichExecutionContextWithPluginBindingsAsync(
        FlowOS.StateMachines.Models.ExecutionContext context,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (_pluginBindingRegistry == null) return;

        var bindings = await _pluginBindingRegistry.ResolveBindingsAsync(
            tenantId,
            PluginBindingTypes.Decision,
            cancellationToken);

        if (bindings.Count > 0)
        {
            context.DecisionProviderBindings = bindings;
        }
    }

    private async Task StartSubWorkflowChildrenForEnteredStepsAsync(
        WorkflowInstance parentInstance,
        WorkflowDefinition parentDefinition,
        IReadOnlyList<string> enteredStepIds,
        Dictionary<string, object>? parentPayload,
        CancellationToken cancellationToken)
    {
        if (parentInstance.Status == WorkflowInstanceStatus.Completed ||
            parentInstance.Status == WorkflowInstanceStatus.Failed)
        {
            return;
        }

        if (enteredStepIds == null || enteredStepIds.Count == 0) return;

        foreach (var stepId in enteredStepIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var step = parentDefinition.Steps.FirstOrDefault(s => s.StepId == stepId);
            if (step == null || step.StepType != WorkflowStepType.SubWorkflow) continue;

            await EnsureSubWorkflowChildStartedAsync(
                parentInstance,
                step,
                parentPayload,
                cancellationToken);
        }
    }

    private async Task EnsureSubWorkflowChildStartedAsync(
        WorkflowInstance parentInstance,
        WorkflowStepDefinition parentStep,
        Dictionary<string, object>? parentPayload,
        CancellationToken cancellationToken)
    {
        if (parentStep.SubWorkflow == null)
        {
            throw new InvalidOperationException(
                $"SubWorkflow step '{parentStep.StepId}' is missing subWorkflow reference configuration.");
        }

        var existingChild = await _unitOfWork.WorkflowInstances.GetLatestChildByParentStepAsync(
            parentInstance.TenantId,
            parentInstance.Id,
            parentStep.StepId,
            cancellationToken);

        if (existingChild != null)
        {
            return;
        }

        var (childDefinition, childWorkflowClassId) = await ResolveSubWorkflowDefinitionAsync(
            parentInstance.TenantId,
            parentStep,
            cancellationToken);

        var childPayload = BuildSubWorkflowInputPayload(parentStep, parentPayload);
        childPayload["ParentWorkflowInstanceId"] = parentInstance.Id.ToString();
        childPayload["ParentStepId"] = parentStep.StepId;

        var childInstance = new WorkflowInstance(
            parentInstance.TenantId,
            childDefinition.Id,
            childWorkflowClassId,
            childDefinition.Version,
            childDefinition.StartStepId,
            correlationId: null,
            initialState: null,
            parentWorkflowInstanceId: parentInstance.Id,
            parentStepId: parentStep.StepId);

        _unitOfWork.WorkflowInstances.Add(childInstance);

        var startEvent = new StandardEvent(parentInstance.TenantId, "WorkflowStarted");
        startEvent.SetCorrelationId(childInstance.Id);
        startEvent.AddMetadata("WorkflowName", childDefinition.Name);
        startEvent.AddMetadata("Version", childDefinition.Version.ToString());
        startEvent.AddMetadata("StartStep", childDefinition.StartStepId);
        startEvent.AddMetadata("InitialState", childInstance.CurrentState ?? childDefinition.StartStepId);
        startEvent.AddMetadata("ParentWorkflowInstanceId", parentInstance.Id.ToString());
        startEvent.AddMetadata("ParentStepId", parentStep.StepId);
        if (!string.IsNullOrEmpty(_currentUser.Id))
        {
            startEvent.AddMetadata("ActorId", _currentUser.Id);
        }
        _unitOfWork.Events.Add(startEvent);

        var initialChildStep = childDefinition.Steps.FirstOrDefault(s => s.StepId == childInstance.CurrentStepId);
        if (_actionDispatcher != null && initialChildStep?.OnEntry != null && initialChildStep.OnEntry.Count > 0)
        {
            await _actionDispatcher.QueueActionsAsync(
                parentInstance.TenantId,
                childInstance.Id,
                initialChildStep.StepId,
                "OnEntry",
                initialChildStep.OnEntry,
                childPayload,
                cancellationToken);
        }

        var childContext = new FlowOS.StateMachines.Models.ExecutionContext
        {
            Payload = childPayload
        };
        await EnrichExecutionContextWithPluginBindingsAsync(childContext, parentInstance.TenantId, cancellationToken);

        var childSmDef = await ResolveStateMachineDefinitionAsync(
            childInstance.WorkflowClassId,
            parentInstance.TenantId,
            childDefinition.Name,
            cancellationToken);

        RunAutoAdvance(childInstance, childDefinition, parentInstance.TenantId, childContext, childSmDef);
        var autoAdvancedEnteredStepIds = (childInstance.ActiveStepIds != null && childInstance.ActiveStepIds.Count > 0)
            ? childInstance.ActiveStepIds.ToList()
            : (string.IsNullOrEmpty(childInstance.CurrentStepId) ? new List<string>() : new List<string> { childInstance.CurrentStepId });
        await StartSubWorkflowChildrenForEnteredStepsAsync(
            childInstance,
            childDefinition,
            autoAdvancedEnteredStepIds,
            childContext.Payload,
            cancellationToken);
        await CheckAndScheduleTimerAsync(childInstance, childDefinition, parentInstance.TenantId, childContext.Payload, cancellationToken);

        if (childInstance.Status == WorkflowInstanceStatus.Completed)
        {
            var completionEvent = new StandardEvent(parentInstance.TenantId, "WorkflowCompleted");
            completionEvent.SetCorrelationId(childInstance.Id);
            completionEvent.AddMetadata("CompletedStep", childInstance.CurrentStepId);
            completionEvent.AddMetadata("FinalState", childInstance.CurrentState ?? "Completed");
            completionEvent.AddMetadata("ParentWorkflowInstanceId", parentInstance.Id.ToString());
            completionEvent.AddMetadata("ParentStepId", parentStep.StepId);
            _unitOfWork.Events.Add(completionEvent);

            await TryResumeParentWorkflowOnChildCompletionAsync(childInstance, cancellationToken);
        }
    }

    private async Task<(WorkflowDefinition Definition, Guid WorkflowClassId)> ResolveSubWorkflowDefinitionAsync(
        Guid tenantId,
        WorkflowStepDefinition subWorkflowStep,
        CancellationToken cancellationToken)
    {
        var reference = subWorkflowStep.SubWorkflow
            ?? throw new InvalidOperationException($"SubWorkflow step '{subWorkflowStep.StepId}' is missing subWorkflow reference.");

        if (reference.WorkflowDefinitionId.HasValue && reference.WorkflowDefinitionId.Value != Guid.Empty)
        {
            var definition = await _unitOfWork.WorkflowDefinitions.GetByIdAsNoTrackingAsync(
                reference.WorkflowDefinitionId.Value,
                cancellationToken);

            if (definition == null || definition.TenantId != tenantId || definition.Status != WorkflowStatus.Published)
            {
                throw new ArgumentException(
                    $"SubWorkflow step '{subWorkflowStep.StepId}' references workflowDefinitionId '{reference.WorkflowDefinitionId}' that is not accessible or not published.");
            }

            return (definition, Guid.Empty);
        }

        if (reference.WorkflowClassId.HasValue && reference.WorkflowClassId.Value != Guid.Empty)
        {
            var workflowClass = await _unitOfWork.WorkflowClasses.GetByIdAsNoTrackingAsync(
                reference.WorkflowClassId.Value,
                cancellationToken);

            if (workflowClass == null || (workflowClass.TenantId != tenantId && workflowClass.Scope != WorkflowClassScope.Public))
            {
                throw new ArgumentException(
                    $"SubWorkflow step '{subWorkflowStep.StepId}' references workflowClassId '{reference.WorkflowClassId}' that is not accessible.");
            }

            var runtimeVersion = WorkflowVersion.Parse(workflowClass.Version).RuntimeVersion;
            var definitionOwnerTenant = workflowClass.TenantId;
            var definition = await _unitOfWork.WorkflowDefinitions.GetByNameAndVersionAsync(
                workflowClass.Name,
                runtimeVersion,
                definitionOwnerTenant,
                cancellationToken);

            if (definition == null || definition.Status != WorkflowStatus.Published)
            {
                throw new ArgumentException(
                    $"SubWorkflow target '{workflowClass.Name}' is not published as runtime definition v{runtimeVersion}.");
            }

            return (definition, workflowClass.Id);
        }

        if (!string.IsNullOrWhiteSpace(reference.WorkflowName))
        {
            WorkflowDefinition? definition;
            if (reference.Version.HasValue)
            {
                definition = await _unitOfWork.WorkflowDefinitions.GetPublishedByNameAndVersionAsync(
                    reference.WorkflowName,
                    reference.Version.Value,
                    tenantId,
                    cancellationToken);
            }
            else
            {
                definition = await _unitOfWork.WorkflowDefinitions.GetLatestByNameAsync(
                    reference.WorkflowName,
                    tenantId,
                    cancellationToken);
            }

            if (definition == null || definition.Status != WorkflowStatus.Published)
            {
                throw new ArgumentException(
                    $"SubWorkflow step '{subWorkflowStep.StepId}' references workflow '{reference.WorkflowName}' that is not published.");
            }

            return (definition, Guid.Empty);
        }

        throw new ArgumentException(
            $"SubWorkflow step '{subWorkflowStep.StepId}' must set workflowDefinitionId, workflowClassId, or workflowName.");
    }

    private async Task TryResumeParentWorkflowOnChildCompletionAsync(
        WorkflowInstance childInstance,
        CancellationToken cancellationToken)
    {
        if (!childInstance.ParentWorkflowInstanceId.HasValue || string.IsNullOrWhiteSpace(childInstance.ParentStepId))
        {
            return;
        }

        var parentInstance = await _unitOfWork.WorkflowInstances.GetByIdAsync(
            childInstance.ParentWorkflowInstanceId.Value,
            childInstance.TenantId,
            cancellationToken);

        if (parentInstance == null ||
            parentInstance.Status == WorkflowInstanceStatus.Completed ||
            parentInstance.Status == WorkflowInstanceStatus.Failed)
        {
            return;
        }

        var parentDefinition = await _unitOfWork.WorkflowDefinitions.GetByIdAsync(
            parentInstance.WorkflowDefinitionId,
            cancellationToken);
        if (parentDefinition == null) return;

        var parentStep = parentDefinition.Steps.FirstOrDefault(s =>
            string.Equals(s.StepId, childInstance.ParentStepId, StringComparison.OrdinalIgnoreCase));
        if (parentStep == null || parentStep.StepType != WorkflowStepType.SubWorkflow) return;

        bool parentIsAtStep =
            string.Equals(parentInstance.CurrentStepId, parentStep.StepId, StringComparison.OrdinalIgnoreCase)
            || (parentInstance.ActiveStepIds?.Contains(parentStep.StepId) == true);
        if (!parentIsAtStep) return;

        var completionEventType = ResolveSubWorkflowCompletionEventType(parentStep);
        if (string.IsNullOrWhiteSpace(completionEventType)) return;

        var contextPayload = BuildSubWorkflowOutputPayload(parentStep, childInstance);
        var context = new FlowOS.StateMachines.Models.ExecutionContext
        {
            Payload = contextPayload
        };
        await EnrichExecutionContextWithPluginBindingsAsync(context, parentInstance.TenantId, cancellationToken);

        var stateMachineDefinition = await ResolveStateMachineDefinitionAsync(
            parentInstance.WorkflowClassId,
            parentInstance.TenantId,
            parentDefinition.Name,
            cancellationToken);
        var currentEntityState = parentInstance.CurrentState ?? parentInstance.CurrentStepId;
        var previousStepId = parentInstance.CurrentStepId;
        var previousState = parentInstance.CurrentState ?? parentInstance.CurrentStepId;

        var domainEvent = new StandardEvent(parentInstance.TenantId, completionEventType);
        domainEvent.SetCorrelationId(parentInstance.Id);
        domainEvent.AddMetadata("Source", "SubWorkflowCompletion");
        domainEvent.AddMetadata("ParentStepId", parentStep.StepId);
        domainEvent.AddMetadata("ChildWorkflowInstanceId", childInstance.Id.ToString());

        var result = _engine.Advance(
            parentInstance,
            parentDefinition,
            domainEvent,
            context,
            stateMachineDefinition,
            currentEntityState);
        if (!result.Success)
        {
            return;
        }

        if (_timerService != null && !string.IsNullOrEmpty(previousStepId))
        {
            await _timerService.CancelTimerAsync(parentInstance.Id, previousStepId, cancellationToken);
        }

        if (_actionDispatcher != null && !string.IsNullOrEmpty(previousStepId))
        {
            var departedStep = parentDefinition.Steps.FirstOrDefault(s => s.StepId == previousStepId);
            if (departedStep?.OnExit != null && departedStep.OnExit.Count > 0)
            {
                await _actionDispatcher.QueueActionsAsync(
                    parentInstance.TenantId,
                    parentInstance.Id,
                    previousStepId,
                    "OnExit",
                    departedStep.OnExit,
                    contextPayload,
                    cancellationToken);
            }
        }

        var enteredStepIds = (parentInstance.ActiveStepIds != null && parentInstance.ActiveStepIds.Count > 0)
            ? parentInstance.ActiveStepIds.ToList()
            : (string.IsNullOrEmpty(parentInstance.CurrentStepId) ? new List<string>() : new List<string> { parentInstance.CurrentStepId });

        if (_actionDispatcher != null)
        {
            foreach (var stepId in enteredStepIds)
            {
                var enteredStep = parentDefinition.Steps.FirstOrDefault(s => s.StepId == stepId);
                if (enteredStep?.OnEntry != null && enteredStep.OnEntry.Count > 0)
                {
                    await _actionDispatcher.QueueActionsAsync(
                        parentInstance.TenantId,
                        parentInstance.Id,
                        stepId,
                        "OnEntry",
                        enteredStep.OnEntry,
                        contextPayload,
                        cancellationToken);
                }
            }
        }

        await StartSubWorkflowChildrenForEnteredStepsAsync(
            parentInstance,
            parentDefinition,
            enteredStepIds,
            contextPayload,
            cancellationToken);

        domainEvent.AddMetadata("FromStep", previousStepId ?? "Start");
        domainEvent.AddMetadata("ToStep", parentInstance.CurrentStepId);
        domainEvent.AddMetadata("FromState", previousState ?? "Draft");
        domainEvent.AddMetadata("ToState", parentInstance.CurrentState ?? "Draft");
        if (!string.IsNullOrEmpty(_currentUser.Id))
        {
            domainEvent.AddMetadata("ActorId", _currentUser.Id);
        }

        _unitOfWork.Events.Add(domainEvent);

        if (parentInstance.Status == WorkflowInstanceStatus.Completed)
        {
            var completionEvent = new StandardEvent(parentInstance.TenantId, "WorkflowCompleted");
            completionEvent.SetCorrelationId(parentInstance.Id);
            completionEvent.AddMetadata("CompletedStep", parentInstance.CurrentStepId);
            completionEvent.AddMetadata("FinalState", parentInstance.CurrentState ?? "Completed");
            _unitOfWork.Events.Add(completionEvent);

            await TryResumeParentWorkflowOnChildCompletionAsync(parentInstance, cancellationToken);
        }

        RunAutoAdvance(parentInstance, parentDefinition, parentInstance.TenantId, context, stateMachineDefinition);
        var autoAdvancedEnteredStepIds = (parentInstance.ActiveStepIds != null && parentInstance.ActiveStepIds.Count > 0)
            ? parentInstance.ActiveStepIds.ToList()
            : (string.IsNullOrEmpty(parentInstance.CurrentStepId) ? new List<string>() : new List<string> { parentInstance.CurrentStepId });
        await StartSubWorkflowChildrenForEnteredStepsAsync(
            parentInstance,
            parentDefinition,
            autoAdvancedEnteredStepIds,
            context.Payload,
            cancellationToken);
        await CheckAndScheduleTimerAsync(parentInstance, parentDefinition, parentInstance.TenantId, context.Payload, cancellationToken);
    }

    private static Dictionary<string, object> BuildSubWorkflowInputPayload(
        WorkflowStepDefinition parentStep,
        Dictionary<string, object>? parentPayload)
    {
        var sourcePayload = parentPayload ?? new Dictionary<string, object>();
        var mapping = parentStep.SubWorkflow?.InputMapping;
        if (mapping == null || mapping.Count == 0)
        {
            return new Dictionary<string, object>(sourcePayload, StringComparer.OrdinalIgnoreCase);
        }

        var resolved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in mapping)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key)) continue;

            var evaluated = ExpressionEvaluator.EvaluateValue(kvp.Value, sourcePayload);
            resolved[kvp.Key] = evaluated ?? kvp.Value;
        }

        return resolved;
    }

    private static Dictionary<string, object> BuildSubWorkflowOutputPayload(
        WorkflowStepDefinition parentStep,
        WorkflowInstance childInstance)
    {
        var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["SubWorkflowCompleted"] = true,
            ["ChildWorkflowInstanceId"] = childInstance.Id.ToString(),
            ["ChildWorkflowClassId"] = childInstance.WorkflowClassId.ToString(),
            ["ChildWorkflowDefinitionId"] = childInstance.WorkflowDefinitionId.ToString(),
            ["ChildStatus"] = childInstance.Status.ToString(),
            ["ChildCurrentStepId"] = childInstance.CurrentStepId,
            ["ChildCurrentState"] = childInstance.CurrentState ?? string.Empty
        };

        var mapping = parentStep.SubWorkflow?.OutputMapping;
        if (mapping == null || mapping.Count == 0)
        {
            return payload;
        }

        foreach (var kvp in mapping)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key)) continue;

            var evaluated = ExpressionEvaluator.EvaluateValue(kvp.Value, payload);
            payload[kvp.Key] = evaluated ?? kvp.Value;
        }

        return payload;
    }

    private static string? ResolveSubWorkflowCompletionEventType(WorkflowStepDefinition parentStep)
    {
        if (parentStep.NextSteps == null || parentStep.NextSteps.Count == 0)
        {
            return null;
        }

        var preferred = parentStep.NextSteps.Keys.FirstOrDefault(k =>
            string.Equals(k, "SubWorkflowCompleted", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred;

        var defaultKey = parentStep.NextSteps.Keys.FirstOrDefault(k =>
            string.Equals(k, "Default", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(defaultKey)) return defaultKey;

        return parentStep.NextSteps.Keys.FirstOrDefault();
    }

    private TimeSpan ParseDuration(string? durationStr)
    {
        if (string.IsNullOrWhiteSpace(durationStr))
            return TimeSpan.FromSeconds(5);

        durationStr = durationStr.Trim();
        bool isNegative = durationStr.StartsWith("-");
        if (isNegative || durationStr.StartsWith("+"))
        {
            durationStr = durationStr[1..].Trim();
        }

        TimeSpan parsed;
        if (durationStr.EndsWith("s", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(durationStr[..^1], out var seconds))
        {
            parsed = TimeSpan.FromSeconds(seconds);
        }
        else if (durationStr.EndsWith("m", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(durationStr[..^1], out var minutes))
        {
            parsed = TimeSpan.FromMinutes(minutes);
        }
        else if (durationStr.EndsWith("h", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(durationStr[..^1], out var hours))
        {
            parsed = TimeSpan.FromHours(hours);
        }
        else if (durationStr.EndsWith("d", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(durationStr[..^1], out var days))
        {
            parsed = TimeSpan.FromDays(days);
        }
        else if (double.TryParse(durationStr, out var rawSecs))
        {
            parsed = TimeSpan.FromSeconds(rawSecs);
        }
        else if (durationStr.Contains(':') && TimeSpan.TryParse(durationStr, out var ts))
        {
            parsed = ts;
        }
        else
        {
            try
            {
                parsed = System.Xml.XmlConvert.ToTimeSpan(durationStr);
            }
            catch
            {
                parsed = TimeSpan.FromSeconds(5);
            }
        }

        return isNegative ? -parsed : parsed;
    }
}
