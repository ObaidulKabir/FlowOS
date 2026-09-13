using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.StateMachines.Engine;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Engine;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public class WorkflowTimeTravelService : IWorkflowTimeTravelService
{
    private readonly FlowOSDbContext _dbContext;
    private readonly WorkflowEngine _engine;
    private readonly StateMachineEngine _stateMachineEngine;

    public WorkflowTimeTravelService(
        FlowOSDbContext dbContext,
        WorkflowEngine engine,
        StateMachineEngine stateMachineEngine)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _stateMachineEngine = stateMachineEngine ?? throw new ArgumentNullException(nameof(stateMachineEngine));
    }

    public async Task<WorkflowTimeTravelReplayDto?> GetReplayTimelineAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowInstanceId && w.TenantId == tenantId, cancellationToken);

        if (instance == null) return null;

        var definition = await _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == instance.WorkflowDefinitionId, cancellationToken);

        var events = await _dbContext.Events
            .AsNoTracking()
            .Where(e => e.CorrelationId == workflowInstanceId && e.TenantId == tenantId)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(cancellationToken);

        var actionLogs = await _dbContext.ActionExecutionLogs
            .AsNoTracking()
            .Where(a => a.WorkflowInstanceId == workflowInstanceId && a.TenantId == tenantId)
            .OrderBy(a => a.ExecutedAtUtc)
            .ToListAsync(cancellationToken);

        var snapshots = new List<WorkflowTimeTravelSnapshotDto>();
        var runningVariables = new Dictionary<string, object?>();
        var runningActiveSteps = new List<string>();
        string currentState = "Draft";
        string currentStep = definition?.StartStepId ?? "Start";

        for (int i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            var meta = evt.Metadata ?? new Dictionary<string, string>();

            string fromStep = meta.GetValueOrDefault("FromStep", currentStep);
            string toStep = meta.GetValueOrDefault("ToStep", currentStep);
            string fromState = meta.GetValueOrDefault("FromState", currentState);
            string toState = meta.GetValueOrDefault("ToState", currentState);
            string? actorId = meta.GetValueOrDefault("ActorId");

            if (evt.EventType == "WorkflowStarted")
            {
                currentStep = meta.GetValueOrDefault("StartStep", definition?.StartStepId ?? "Start");
                toStep = currentStep;
                fromStep = "None";
                currentState = meta.GetValueOrDefault("InitialState", "Draft");
                toState = currentState;
                fromState = "None";
                runningActiveSteps = new List<string> { currentStep };
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(toStep))
                {
                    currentStep = toStep;
                    runningActiveSteps = new List<string> { toStep };
                }
                if (!string.IsNullOrWhiteSpace(toState))
                {
                    currentState = toState;
                }
            }

            // Extract and accumulate payload variables
            if (meta.TryGetValue("Payload", out var payloadJson) && !string.IsNullOrWhiteSpace(payloadJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson);
                    if (parsed != null)
                    {
                        foreach (var kv in parsed)
                        {
                            runningVariables[kv.Key] = kv.Value?.ToString();
                        }
                    }
                }
                catch
                {
                    runningVariables["rawPayload"] = payloadJson;
                }
            }

            // Correlate actions for this step
            var correlatedActions = actionLogs
                .Where(a => a.StepId == currentStep || a.StepId == toStep)
                .Where(a => Math.Abs((a.ExecutedAtUtc - evt.Timestamp).TotalSeconds) <= 5)
                .Select(MapToActionDto)
                .ToList();

            string summary = GenerateStepSummary(evt, fromStep, toStep, fromState, toState);

            snapshots.Add(new WorkflowTimeTravelSnapshotDto(
                StepIndex: i,
                Timestamp: evt.Timestamp,
                EventId: evt.EventId,
                EventType: evt.EventType,
                FromStepId: fromStep,
                ToStepId: toStep,
                ActiveStepIds: new List<string>(runningActiveSteps),
                FromState: fromState,
                ToState: toState,
                ActorId: actorId,
                Variables: new Dictionary<string, object?>(runningVariables),
                ActionLogs: correlatedActions,
                Summary: summary
            ));
        }

        return new WorkflowTimeTravelReplayDto(
            WorkflowInstanceId: instance.Id,
            WorkflowClassName: definition?.Name ?? "Workflow",
            WorkflowVersion: instance.WorkflowVersion,
            Status: instance.Status.ToString(),
            TotalSteps: snapshots.Count,
            Snapshots: snapshots
        );
    }

    public async Task<WorkflowForkSimulationResultDto> SimulateForkAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        int targetStepIndex,
        string alternativeEvent,
        object? alternativePayload = null,
        CancellationToken cancellationToken = default)
    {
        var replay = await GetReplayTimelineAsync(tenantId, workflowInstanceId, cancellationToken);
        if (replay == null || replay.Snapshots.Count == 0)
        {
            return new WorkflowForkSimulationResultDto(
                targetStepIndex,
                "Unknown",
                "Unknown",
                alternativeEvent,
                "None",
                "None",
                false,
                $"Workflow instance '{workflowInstanceId}' has no replay history.",
                new List<string>()
            );
        }

        int clampedIndex = Math.Clamp(targetStepIndex, 0, replay.Snapshots.Count - 1);
        var baseSnapshot = replay.Snapshots[clampedIndex];

        var definition = await _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Name == replay.WorkflowClassName && d.Version == replay.WorkflowVersion, cancellationToken)
            ?? await _dbContext.WorkflowDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.TenantId == tenantId, cancellationToken);

        if (definition == null)
        {
            return new WorkflowForkSimulationResultDto(
                clampedIndex,
                baseSnapshot.ToStepId,
                baseSnapshot.ToState,
                alternativeEvent,
                "None",
                "None",
                false,
                "Workflow runtime definition not found.",
                new List<string>()
            );
        }

        // Locate current step in definition
        var baseStep = definition.Steps.FirstOrDefault(s => s.StepId == baseSnapshot.ToStepId);
        if (baseStep == null)
        {
            return new WorkflowForkSimulationResultDto(
                clampedIndex,
                baseSnapshot.ToStepId,
                baseSnapshot.ToState,
                alternativeEvent,
                "None",
                "None",
                false,
                $"Step '{baseSnapshot.ToStepId}' does not exist in definition.",
                new List<string>()
            );
        }

        if (!baseStep.NextSteps.TryGetValue(alternativeEvent, out var projectedNextStepId))
        {
            return new WorkflowForkSimulationResultDto(
                clampedIndex,
                baseSnapshot.ToStepId,
                baseSnapshot.ToState,
                alternativeEvent,
                "None",
                "None",
                false,
                $"Step '{baseSnapshot.ToStepId}' does not define an outgoing transition for event '{alternativeEvent}'. Valid transitions: {string.Join(", ", baseStep.NextSteps.Keys)}",
                new List<string>()
            );
        }

        // Evaluate State Machine constraint
        var smDef = await _dbContext.StateMachineDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(sm => sm.TenantId == tenantId && sm.Name == definition.Name, cancellationToken)
            ?? await _dbContext.StateMachineDefinitions.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        string projectedState = baseSnapshot.ToState;
        if (smDef != null)
        {
            var simulatedDomainEvent = new StandardEvent(tenantId, alternativeEvent);
            var context = new StateMachines.Models.ExecutionContext();
            if (alternativePayload != null)
            {
                try
                {
                    var json = JsonSerializer.Serialize(alternativePayload);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    if (dict != null) context.Payload = dict;
                }
                catch { }
            }

            var smResult = _stateMachineEngine.ValidateTransition(smDef, baseSnapshot.ToState, simulatedDomainEvent, context);
            if (!smResult.IsAllowed)
            {
                return new WorkflowForkSimulationResultDto(
                    clampedIndex,
                    baseSnapshot.ToStepId,
                    baseSnapshot.ToState,
                    alternativeEvent,
                    projectedNextStepId,
                    baseSnapshot.ToState,
                    false,
                    $"State machine rule violation: {smResult.Reason}",
                    new List<string>()
                );
            }
            if (smResult.MatchedTransition != null)
            {
                projectedState = smResult.MatchedTransition.ToState;
            }
        }

        // Collect projected actions for next step
        var projectedNextStep = definition.Steps.FirstOrDefault(s => s.StepId == projectedNextStepId);
        var projectedActionSummaries = new List<string>();
        if (baseStep.OnExit != null)
        {
            projectedActionSummaries.AddRange(baseStep.OnExit.Select(a => $"OnExit({baseStep.StepId}): {a.ActionType} -> {a.Target ?? a.Url ?? "Event"}"));
        }
        if (projectedNextStep?.OnEntry != null)
        {
            projectedActionSummaries.AddRange(projectedNextStep.OnEntry.Select(a => $"OnEntry({projectedNextStepId}): {a.ActionType} -> {a.Target ?? a.Url ?? "Event"}"));
        }

        return new WorkflowForkSimulationResultDto(
            ForkFromStepIndex: clampedIndex,
            BaseStepId: baseSnapshot.ToStepId,
            BaseState: baseSnapshot.ToState,
            AlternativeEvent: alternativeEvent,
            ProjectedStepId: projectedNextStepId,
            ProjectedState: projectedState,
            IsAllowed: true,
            Reason: $"Valid alternative branch path: Transitioned from '{baseSnapshot.ToStepId}' to '{projectedNextStepId}' (State: {baseSnapshot.ToState} ➔ {projectedState}).",
            ProjectedActions: projectedActionSummaries
        );
    }

    private static string GenerateStepSummary(DomainEvent evt, string fromStep, string toStep, string fromState, string toState)
    {
        if (evt.EventType == "WorkflowStarted")
        {
            return $"Workflow initialized at step '{toStep}' with legal state '{toState}'.";
        }
        if (evt.EventType == "WorkflowCompleted")
        {
            return $"Workflow reached END milestone in final state '{toState}'.";
        }
        return $"Event '{evt.EventType}' advanced workflow from '{fromStep}' to '{toStep}' (State: '{fromState}' ➔ '{toState}').";
    }

    private static WorkflowActionExecutionLogDto MapToActionDto(WorkflowActionExecutionLog log)
    {
        return new WorkflowActionExecutionLogDto(
            log.Id,
            log.TenantId,
            log.WorkflowInstanceId,
            log.StepId,
            log.TriggerPhase,
            log.ActionType,
            log.Target,
            log.Status,
            log.ExecutedAtUtc,
            log.DurationMs,
            log.HttpStatusCode,
            log.RequestPayloadSnippet,
            log.ResponseSnippet,
            log.ErrorMessage,
            log.AttemptNumber,
            log.OutboxMessageId
        );
    }
}
