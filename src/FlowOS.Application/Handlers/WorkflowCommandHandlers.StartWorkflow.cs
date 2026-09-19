using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
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
        ActiveWorkflowContextBinding? activeContextBinding = null;
        PreparedWorkflowContext? preparedContext = null;
        var hasContextSelector = request.ContextBindingId.HasValue ||
                                 !string.IsNullOrWhiteSpace(request.ContextType);
        var hasLegacySelector = request.WorkflowDefinitionId.HasValue ||
                                request.WorkflowClassId != Guid.Empty ||
                                !string.IsNullOrWhiteSpace(request.WorkflowName);

        if (hasContextSelector)
        {
            if (hasLegacySelector || request.Version.HasValue)
                throw new ArgumentException("Context binding selectors cannot be combined with workflow definition, class, name, or version selectors.");
            if (_workflowContextService == null)
                throw new InvalidOperationException("Workflow context binding service is not registered.");

            activeContextBinding = await _workflowContextService.ResolveActiveAsync(
                request.TenantId,
                request.ContextBindingId,
                request.ContextType,
                cancellationToken);
            fullDefinition = activeContextBinding.WorkflowDefinition;
            definitionId = fullDefinition.Id;
            preparedContext = _workflowContextService.PrepareInitial(activeContextBinding, request.Payload);
        }
        else if (request.WorkflowDefinitionId.HasValue)
        {
            definitionId = request.WorkflowDefinitionId.Value;
            fullDefinition = await _unitOfWork.WorkflowDefinitions
                .GetByIdAsNoTrackingAsync(definitionId, cancellationToken);

            if (fullDefinition == null || fullDefinition.TenantId != request.TenantId)
            {
                throw new ArgumentException($"Workflow definition '{definitionId}' not found.");
            }
            if (fullDefinition.ContextBindingRevisionId.HasValue)
            {
                throw new ArgumentException("Context-materialized definitions must be started through ContextBindingId or ContextType.");
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
             throw new ArgumentException("Either ContextBindingId, ContextType, WorkflowDefinitionId, WorkflowClassId, or WorkflowName must be provided.");
        }

        if (activeContextBinding == null && fullDefinition?.ContextBindingRevisionId.HasValue == true)
        {
            throw new ArgumentException("Context-materialized definitions must be started through ContextBindingId or ContextType.");
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

        Guid resolvedClassId = activeContextBinding?.Revision.SourceWorkflowClassId ?? request.WorkflowClassId;
        string? resolvedInitialState = activeContextBinding?.SourceWorkflowClass.Definition.StateMachine.InitialState;

        if (resolvedClassId == Guid.Empty && fullDefinition != null)
        {
            var publishedClasses = await _unitOfWork.WorkflowClasses.ListAsync(
                request.TenantId,
                null,
                Domain.Enums.WorkflowClassStatus.Published,
                cancellationToken);
            var matchingClass = publishedClasses.FirstOrDefault(c =>
                string.Equals(c.Name, fullDefinition.Name, StringComparison.OrdinalIgnoreCase) &&
                (c.TenantId == request.TenantId || c.Scope == Domain.Enums.WorkflowClassScope.Public));
            if (matchingClass != null)
            {
                resolvedClassId = matchingClass.Id;
                if (string.IsNullOrWhiteSpace(resolvedInitialState))
                    resolvedInitialState = matchingClass.Definition.StateMachine.InitialState;
            }
        }

        var instance = new WorkflowInstance(
            request.TenantId,
            definitionId,
            resolvedClassId,
            actualVersion,
            startStep,
            request.CorrelationId,
            resolvedInitialState
        );

        _unitOfWork.WorkflowInstances.Add(instance);

        var autoAdvanceContext = new FlowOS.StateMachines.Models.ExecutionContext();
        if (preparedContext != null && _workflowContextService != null)
        {
            autoAdvanceContext.Payload = preparedContext.Payload;
            var snapshot = _workflowContextService.CreateSnapshot(
                instance.Id,
                request.TenantId,
                preparedContext,
                request.BusinessReference);
            _unitOfWork.WorkflowContextSnapshots.Add(snapshot);
        }
        else if (request.Payload != null)
        {
            autoAdvanceContext.Payload = ToPayloadDictionary(request.Payload) ?? new Dictionary<string, object>();
        }
        if (fullDefinition != null)
        {
            ApplyDeclaredBusinessRoleAssignments(instance, fullDefinition, autoAdvanceContext.Payload);
        }
        AddCurrentRolesToContext(autoAdvanceContext);
        await EnrichExecutionContextWithPluginBindingsAsync(autoAdvanceContext, request.TenantId, cancellationToken);

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
        if (preparedContext != null)
        {
            startEvent.AddMetadata("ContextBindingRevisionId", preparedContext.Revision.Id.ToString());
            startEvent.AddMetadata("ContextBindingId", preparedContext.Revision.BindingId.ToString());
            startEvent.AddMetadata("Payload", System.Text.Json.JsonSerializer.Serialize(preparedContext.Delta));
            startEvent.AddMetadata("CanonicalEventType", "WorkflowStarted");
            startEvent.AddMetadata("ContextualEventType", "WorkflowStarted");
            if (activeContextBinding != null)
            {
                startEvent.AddMetadata("ContextType", activeContextBinding.Binding.ContextType);
            }
            if (!string.IsNullOrWhiteSpace(request.BusinessReference?.SourceSystem))
            {
                startEvent.AddMetadata("SourceSystem", request.BusinessReference.SourceSystem);
            }
            if (!string.IsNullOrWhiteSpace(request.BusinessReference?.ExternalEntityId))
            {
                startEvent.AddMetadata("ExternalEntityId", request.BusinessReference.ExternalEntityId);
            }
            if (request.BusinessReference?.Metadata is { Count: > 0 })
            {
                startEvent.AddMetadata(
                    "BusinessMetadata",
                    System.Text.Json.JsonSerializer.Serialize(request.BusinessReference.Metadata));
            }
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
                        autoAdvanceContext.Payload,
                        cancellationToken);
                }
            }

            await StartSubWorkflowChildrenForEnteredStepsAsync(
                instance,
                fullDefinition,
                initialEnteredStepIds,
                autoAdvanceContext.Payload,
                cancellationToken);

            var startStateMachine = fullDefinition.StateMachineDefinitionId.HasValue
                ? await ResolveStateMachineDefinitionAsync(
                    instance.WorkflowClassId,
                    request.TenantId,
                    fullDefinition.Name,
                    cancellationToken,
                    fullDefinition.StateMachineDefinitionId)
                : null;
            RunAutoAdvance(instance, fullDefinition, request.TenantId, autoAdvanceContext, startStateMachine);
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
        await TryRunBoundedAutonomyAsync(request.TenantId, instance.Id, cancellationToken);

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
}
