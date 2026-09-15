using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
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
    private readonly ICompensationPlannerService _compensationPlanner;
    private readonly IPluginBindingRegistryService? _pluginBindingRegistry;
    private readonly IWorkflowExecutionContextService? _workflowContextService;

    public WorkflowTimeTravelService(
        FlowOSDbContext dbContext,
        WorkflowEngine engine,
        StateMachineEngine stateMachineEngine,
        ICompensationPlannerService compensationPlanner,
        IPluginBindingRegistryService? pluginBindingRegistry = null,
        IWorkflowExecutionContextService? workflowContextService = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _stateMachineEngine = stateMachineEngine ?? throw new ArgumentNullException(nameof(stateMachineEngine));
        _compensationPlanner = compensationPlanner ?? throw new ArgumentNullException(nameof(compensationPlanner));
        _pluginBindingRegistry = pluginBindingRegistry;
        _workflowContextService = workflowContextService;
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

        var correlationIds = new List<Guid> { instance.Id };
        if (instance.CorrelationId.HasValue && instance.CorrelationId.Value != instance.Id)
        {
            correlationIds.Add(instance.CorrelationId.Value);
        }

        var events = await _dbContext.Events
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.CorrelationId.HasValue && correlationIds.Contains(e.CorrelationId.Value))
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.EventId)
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
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson);
                    if (parsed != null)
                    {
                        foreach (var kv in parsed)
                        {
                            runningVariables[kv.Key] = ConvertJsonElement(kv.Value);
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
        if (string.IsNullOrWhiteSpace(alternativeEvent))
        {
            return DeniedFork(targetStepIndex, "Unknown", "Unknown", alternativeEvent ?? string.Empty,
                "Alternative event is required for a what-if fork simulation.");
        }

        var instance = await _dbContext.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowInstanceId && w.TenantId == tenantId, cancellationToken);

        if (instance == null)
        {
            return DeniedFork(targetStepIndex, "Unknown", "Unknown", alternativeEvent,
                $"Workflow instance '{workflowInstanceId}' was not found.");
        }

        var replay = await GetReplayTimelineAsync(tenantId, workflowInstanceId, cancellationToken);
        if (replay == null || replay.Snapshots.Count == 0)
        {
            return DeniedFork(targetStepIndex, instance.CurrentStepId, instance.CurrentState ?? "Unknown", alternativeEvent,
                $"Workflow instance '{workflowInstanceId}' has no replay history.");
        }

        int clampedIndex = Math.Clamp(targetStepIndex, 0, replay.Snapshots.Count - 1);
        var baseSnapshot = replay.Snapshots[clampedIndex];

        var definition = await _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == instance.WorkflowDefinitionId, cancellationToken);

        if (definition == null)
        {
            return DeniedFork(clampedIndex, baseSnapshot.ToStepId, baseSnapshot.ToState, alternativeEvent,
                "Workflow runtime definition not found.");
        }

        var seedStepId = string.IsNullOrWhiteSpace(baseSnapshot.ToStepId) ? definition.StartStepId : baseSnapshot.ToStepId;
        var baseStep = definition.Steps.FirstOrDefault(s => s.StepId == seedStepId);
        if (baseStep == null)
        {
            return DeniedFork(clampedIndex, seedStepId, baseSnapshot.ToState, alternativeEvent,
                $"Step '{seedStepId}' does not exist in definition.");
        }

        var smDef = definition.StateMachineDefinitionId.HasValue
            ? await _dbContext.StateMachineDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    sm => sm.Id == definition.StateMachineDefinitionId.Value && sm.TenantId == tenantId,
                    cancellationToken)
            : await _dbContext.StateMachineDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    sm => sm.TenantId == tenantId && sm.EntityType == definition.Name,
                    cancellationToken);

        var context = new StateMachines.Models.ExecutionContext();
        if (_workflowContextService != null)
        {
            context.Payload = await _workflowContextService.PrepareSimulationAsync(
                tenantId,
                definition,
                baseSnapshot.Variables,
                alternativeEvent,
                alternativePayload,
                cancellationToken);
        }
        else
        {
            context.Payload = baseSnapshot.Variables.ToDictionary(
                item => item.Key,
                item => item.Value!);
            var payload = ToPayloadDictionary(alternativePayload);
            if (payload != null)
            {
                foreach (var item in payload)
                {
                    context.Payload[item.Key] = item.Value;
                }
            }
        }
        if (_pluginBindingRegistry != null)
        {
            context.DecisionProviderBindings = await _pluginBindingRegistry.ResolveBindingsAsync(
                tenantId,
                PluginBindingTypes.Decision,
                cancellationToken);
        }

        // Ephemeral clone — never attached to the DbContext, so production state and outbox stay untouched.
        var sandbox = new WorkflowInstance(
            tenantId,
            definition.Id,
            instance.WorkflowClassId,
            instance.WorkflowVersion,
            seedStepId,
            Guid.NewGuid(),
            baseSnapshot.ToState);

        if (baseSnapshot.ActiveStepIds.Count > 1)
        {
            sandbox.ForkTo(baseSnapshot.ActiveStepIds);
        }

        var simulatedEvent = new StandardEvent(tenantId, alternativeEvent.Trim());
        var advance = _engine.Advance(
            sandbox,
            definition,
            simulatedEvent,
            context,
            smDef,
            baseSnapshot.ToState);

        if (!advance.Success)
        {
            var valid = string.Join(", ", baseStep.NextSteps.Keys);
            return new WorkflowForkSimulationResultDto(
                clampedIndex,
                baseSnapshot.ToStepId,
                baseSnapshot.ToState,
                alternativeEvent,
                "None",
                baseSnapshot.ToState,
                false,
                string.IsNullOrWhiteSpace(valid)
                    ? advance.FailureReason
                    : $"{advance.FailureReason} Valid transitions: {valid}",
                new List<string>()
            );
        }

        var projectedStepId = advance.NewStepId
            ?? (sandbox.Status == WorkflowInstanceStatus.Completed ? "END" : sandbox.CurrentStepId);
        var projectedState = sandbox.CurrentState ?? baseSnapshot.ToState;
        if (smDef == null && !string.IsNullOrWhiteSpace(projectedStepId) && projectedStepId != "END")
        {
            projectedState = projectedStepId;
        }
        var projectedNextStep = definition.Steps.FirstOrDefault(s => s.StepId == projectedStepId);
        var projectedActionSummaries = new List<string>();

        if (baseStep.OnExit != null)
        {
            projectedActionSummaries.AddRange(baseStep.OnExit.Select(a =>
                $"OnExit({baseStep.StepId}): {a.ActionType} -> {a.Target ?? a.Url ?? "Event"}"));
        }

        if (projectedNextStep?.OnEntry != null)
        {
            projectedActionSummaries.AddRange(projectedNextStep.OnEntry.Select(a =>
                $"OnEntry({projectedStepId}): {a.ActionType} -> {a.Target ?? a.Url ?? "Event"}"));
        }

        return new WorkflowForkSimulationResultDto(
            ForkFromStepIndex: clampedIndex,
            BaseStepId: baseSnapshot.ToStepId,
            BaseState: baseSnapshot.ToState,
            AlternativeEvent: alternativeEvent,
            ProjectedStepId: projectedStepId,
            ProjectedState: projectedState,
            IsAllowed: true,
            Reason: $"Sandboxed what-if: '{baseSnapshot.ToStepId}' --[{alternativeEvent}]--> '{projectedStepId}' (State: {baseSnapshot.ToState} ➔ {projectedState}). No production writes or outbox dispatches were performed.",
            ProjectedActions: projectedActionSummaries
        );
    }

    public async Task<WorkflowCompensationPathDto?> PlanCompensationPathAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? failedStepId = null,
        CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowInstanceId && w.TenantId == tenantId, cancellationToken);
        if (instance == null) return null;

        var replay = await GetReplayTimelineAsync(tenantId, workflowInstanceId, cancellationToken);
        var definition = await _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == instance.WorkflowDefinitionId, cancellationToken);

        if (replay == null || definition == null)
        {
            return null;
        }

        var resolvedFailedStep = string.IsNullOrWhiteSpace(failedStepId)
            ? replay.Snapshots.LastOrDefault()?.ToStepId ?? instance.CurrentStepId
            : failedStepId.Trim();
        if (string.IsNullOrWhiteSpace(resolvedFailedStep))
        {
            resolvedFailedStep = definition.StartStepId;
        }

        var executed = replay.Snapshots
            .Select(s => s.ToStepId)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (executed.Count == 0)
        {
            executed.Add(definition.StartStepId);
        }

        var lastFailedIndex = executed.FindLastIndex(s => string.Equals(s, resolvedFailedStep, StringComparison.OrdinalIgnoreCase));
        if (lastFailedIndex >= 0)
        {
            executed = executed.Take(lastFailedIndex + 1).ToList();
        }
        else
        {
            executed.Add(resolvedFailedStep);
        }

        var actionMap = definition.Steps.ToDictionary(
            step => step.StepId,
            step => (step.OnFailure ?? new List<StepActionDefinition>())
                .Select(a => new CompensationActionDto(
                    StepId: step.StepId,
                    Hook: "OnFailure",
                    ActionType: a.ActionType,
                    Target: a.Target,
                    Url: a.Url,
                    Condition: a.Condition,
                    Capability: a.Capability ?? a.Target))
                .ToList(),
            StringComparer.OrdinalIgnoreCase);

        var plan = _compensationPlanner.Plan(new CompensationPathRequestDto(
            FailedStepId: resolvedFailedStep,
            ExecutedStepIds: executed,
            OnFailureActionsByStep: actionMap));

        return new WorkflowCompensationPathDto(
            WorkflowInstanceId: workflowInstanceId,
            FailedStepId: plan.FailedStepId,
            ExecutedStepIds: executed,
            IsFullyCompensable: plan.IsFullyCompensable,
            OrderedCompensations: plan.OrderedCompensations,
            BlockedSteps: plan.BlockedSteps);
    }

    private static WorkflowForkSimulationResultDto DeniedFork(
        int stepIndex,
        string baseStepId,
        string baseState,
        string alternativeEvent,
        string reason)
    {
        return new WorkflowForkSimulationResultDto(
            stepIndex,
            baseStepId,
            baseState,
            alternativeEvent,
            "None",
            "None",
            false,
            reason,
            new List<string>());
    }

    private static Dictionary<string, object>? ToPayloadDictionary(object? payload)
    {
        if (payload == null) return null;
        if (payload is Dictionary<string, object> dict) return dict;

        try
        {
            var json = payload is string text && !string.IsNullOrWhiteSpace(text)
                ? text
                : JsonSerializer.Serialize(payload);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static object? ConvertJsonElement(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.Clone()
        };

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
