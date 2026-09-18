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

        var currentRoles = _currentUser.Roles ?? new List<string>();
        var hasCaller = currentRoles.Any() || !string.IsNullOrEmpty(_currentUser.Id);
        if (hasCaller)
        {
            var currentStep = definition.Steps.FirstOrDefault(x => x.StepId == instance.CurrentStepId);
            var requiredCapabilities = ActivityAuthorization.ResolveRequiredCapabilities(currentStep);
            var failClosed = currentStep?.StepType == WorkflowStepType.HumanTask &&
                             (requiredCapabilities.Count > 0 ||
                              (currentStep.EventRequiredCapabilities?.Count > 0));
            await _activityAuthorization.AuthorizeAsync(
                request.TenantId,
                currentRoles,
                requiredCapabilities,
                failClosed,
                "ActivityAuthorization",
                $"complete task on step '{instance.CurrentStepId}'",
                cancellationToken);
        }

        PreparedWorkflowContext? preparedContext = null;
        if (_workflowContextService != null)
        {
            preparedContext = await _workflowContextService.PrepareForInstanceAsync(
                request.TenantId,
                definition,
                instance.Id,
                null,
                null,
                cancellationToken);
        }

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
        if (preparedContext != null)
        {
            context.Payload = preparedContext.Payload;
            AddContextAuditMetadata(domainEvent, preparedContext, domainEvent.EventType);
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
            AssignActorMetadata(domainEvent);

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
            await TryRunBoundedAutonomyAsync(request.TenantId, instance.Id, cancellationToken);
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
}
