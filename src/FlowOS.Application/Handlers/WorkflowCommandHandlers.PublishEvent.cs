using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Services;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Domain.ValueObjects;
using FlowOS.Events.Models;
using FlowOS.Security.Interfaces;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;

namespace FlowOS.Application.Handlers;

public partial class WorkflowCommandHandlers
{
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
        Console.WriteLine($"[Handler] Handling PublishEventCommand: Event={request.EventType}, WorkflowInstanceId={request.WorkflowInstanceId}, Tenant={request.TenantId}");

        var instance = await _unitOfWork.WorkflowInstances
            .GetByIdAsync(request.WorkflowInstanceId, request.TenantId, cancellationToken);

        var userRoles = _currentUser.Roles ?? new List<string>();
        var isAgentCommit = !string.IsNullOrWhiteSpace(request.ActorId) &&
            request.ActorId.StartsWith("Agent:", StringComparison.OrdinalIgnoreCase);
        if (instance == null)
        {
            if (!isAgentCommit && (userRoles.Any() || !string.IsNullOrEmpty(_currentUser.Id)))
            {
                await _activityAuthorization.AuthorizeAsync(
                    request.TenantId,
                    userRoles,
                    new[] { $"event.publish.{request.EventType}" },
                    failClosed: true,
                    "EventPermission",
                    $"publish '{request.EventType}'",
                    cancellationToken);
            }
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

        WorkflowContextBindingRevision? contextRevisionForAuthorization = null;
        if (definition.ContextBindingRevisionId.HasValue)
        {
            contextRevisionForAuthorization = await _unitOfWork.WorkflowContextBindings
                .GetRevisionByIdAsNoTrackingAsync(definition.ContextBindingRevisionId.Value, cancellationToken);
        }

        if (!isAgentCommit && (userRoles.Any() || !string.IsNullOrEmpty(_currentUser.Id)))
        {
            var currentStep = definition.Steps.FirstOrDefault(step =>
                string.Equals(step.StepId, instance.CurrentStepId, StringComparison.OrdinalIgnoreCase));
            var requiredCapabilities = ActivityAuthorization.ResolveRequiredCapabilities(
                currentStep,
                request.EventType);
            if (requiredCapabilities.Count == 0)
            {
                requiredCapabilities = new List<string>
                {
                    ResolveEventCapability(request.EventType, contextRevisionForAuthorization)
                };
            }

            var failClosed = ActivityAuthorization.IsHumanActivity(currentStep, request.EventType);
            await _activityAuthorization.AuthorizeAsync(
                request.TenantId,
                userRoles,
                requiredCapabilities,
                failClosed,
                "EventPermission",
                $"publish '{request.EventType}'",
                cancellationToken);
        }

        var isRegistered = await _eventRegistry.ExistsAsync(request.EventType, request.TenantId);
        if (!isRegistered && request.EventType.StartsWith("EVT-", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[Handler] Event '{request.EventType}' not registered for tenant {request.TenantId}");
            throw new ArgumentException($"Event '{request.EventType}' is not registered.");
        }

        PreparedWorkflowContext? preparedContext = null;
        if (_workflowContextService != null)
        {
            preparedContext = await _workflowContextService.PrepareForInstanceAsync(
                request.TenantId,
                definition,
                instance.Id,
                request.EventType,
                request.Payload,
                cancellationToken);
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

        if (preparedContext != null)
        {
            AddContextAuditMetadata(domainEvent, preparedContext, request.EventType);
        }
        else if (request.Payload != null)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(request.Payload);
            domainEvent.AddMetadata("Payload", json);
        }

        var context = new FlowOS.StateMachines.Models.ExecutionContext();
        
        if (preparedContext != null)
        {
            context.Payload = preparedContext.Payload;
        }
        else if (request.Payload != null)
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
        AddCurrentRolesToContext(context);
        await EnrichExecutionContextWithPluginBindingsAsync(context, request.TenantId, cancellationToken);

        var smDef = await ResolveStateMachineDefinitionAsync(
            instance.WorkflowClassId,
            request.TenantId,
            definition.Name,
            cancellationToken,
            definition.StateMachineDefinitionId);
        EnsureClassBackedLaw(instance, smDef);
        var currentEntityState = instance.CurrentState ?? instance.CurrentStepId;

        var previousStepId = instance.CurrentStepId;
        var previousState = instance.CurrentState ?? instance.CurrentStepId;
        var result = _engine.Advance(instance, definition, domainEvent, context, smDef, currentEntityState);

        if (result.Success)
        {
            preparedContext?.CommitDelta();

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
            AssignActorMetadata(domainEvent, request.ActorId);

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
            await TryRunBoundedAutonomyAsync(request.TenantId, instance.Id, cancellationToken);
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
}
