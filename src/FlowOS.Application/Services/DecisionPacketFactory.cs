using System.Globalization;
using System.Text.Json;
using FlowOS.Agents.Abstractions;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;
using FlowOS.Events.Models;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;

namespace FlowOS.Application.Services;

public static class DecisionPacketFactory
{
    public static DecisionPacket Create(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        StateMachineDefinition? stateMachine,
        IReadOnlyList<DomainEvent> events,
        IReadOnlyDictionary<string, JsonElement>? canonicalData,
        string? policyGuideline,
        string? objective = null)
    {
        var step = definition.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);
        var eventPayloads = AggregateEventPayloads(events);
        var canonical = ToObjectDictionary(canonicalData);

        var legalNext = step?.NextSteps.Keys
            .Where(key => !IsReservedRoute(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        var legalSm = new List<string>();
        if (stateMachine != null && !string.IsNullOrWhiteSpace(instance.CurrentState))
        {
            legalSm = stateMachine.Transitions
                .Where(transition => string.Equals(transition.FromState, instance.CurrentState, StringComparison.OrdinalIgnoreCase))
                .Select(transition => string.IsNullOrWhiteSpace(transition.EventId) ? transition.TriggerEventType : transition.EventId)
                .Where(eventId => !string.IsNullOrWhiteSpace(eventId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }

        var reminders = step?.Sla?.Reminders?
            .Select(reminder => new SlaReminderFact(reminder.Duration, reminder.TriggerEvent))
            .ToList() ?? new List<SlaReminderFact>();

        AutoCommitPolicy? autoCommit = null;
        if (step?.AutoCommit != null)
        {
            autoCommit = new AutoCommitPolicy(
                step.AutoCommit.MinConfidence,
                step.AutoCommit.AllowedEvents ?? new List<string>());
        }

        return new DecisionPacket(
            instance.TenantId,
            instance.Id,
            instance.CurrentStepId,
            instance.CurrentState,
            step?.StepType.ToString() ?? "Unknown",
            FlowOS.Domain.Enums.StepActor.Normalize(step?.Actor),
            step?.DecisionGuideline,
            policyGuideline,
            canonical,
            legalNext,
            legalSm,
            step?.AllowedRoles ?? new List<string>(),
            reminders,
            step?.Sla?.TimeoutEvent,
            eventPayloads,
            string.IsNullOrWhiteSpace(objective) ? "Decide the next legal workflow event" : objective,
            autoCommit,
            Provider: null,
            Tools: AgentToolCatalog.FromStep(legalNext, step?.AgentTools));
    }

    public static DecisionPacket PreviewFromClass(
        WorkflowClass workflowClass,
        string stepId,
        string? policyGuideline = null,
        IReadOnlyDictionary<string, object?>? canonicalSample = null,
        string? currentState = null,
        string? objective = null)
    {
        if (string.IsNullOrWhiteSpace(stepId))
            throw new ArgumentException("stepId is required.", nameof(stepId));

        var definition = WorkflowClassCompiler.MapToRuntimeDefinition(workflowClass);
        var step = definition.Steps.FirstOrDefault(item =>
            string.Equals(item.StepId, stepId, StringComparison.OrdinalIgnoreCase));
        if (step == null)
            throw new KeyNotFoundException($"Step '{stepId}' was not found on workflow class '{workflowClass.Name}'.");

        var stateMachine = TryMapStateMachine(workflowClass);
        var initialState = currentState
            ?? stateMachine?.InitialState
            ?? workflowClass.Definition.StateMachine.InitialState
            ?? stepId;

        var instance = new WorkflowInstance(
            workflowClass.TenantId,
            definition.Id,
            workflowClass.Id,
            definition.Version,
            step.StepId,
            initialState: initialState);

        var packet = Create(
            instance,
            definition,
            stateMachine,
            Array.Empty<DomainEvent>(),
            null,
            policyGuideline,
            objective);

        if (canonicalSample == null || canonicalSample.Count == 0)
            return packet;

        var canonical = new Dictionary<string, object?>(canonicalSample, StringComparer.OrdinalIgnoreCase);
        return packet with { CanonicalContext = canonical };
    }

    private static StateMachineDefinition? TryMapStateMachine(WorkflowClass workflowClass)
    {
        var blueprint = workflowClass.Definition.StateMachine;
        if (blueprint == null || string.IsNullOrWhiteSpace(blueprint.InitialState))
            return null;

        try
        {
            var entityType = string.IsNullOrWhiteSpace(blueprint.EntityType)
                ? workflowClass.Name
                : blueprint.EntityType;
            var stateMachine = new StateMachineDefinition(
                workflowClass.TenantId,
                entityType,
                blueprint.InitialState,
                1);

            foreach (var state in blueprint.States ?? new List<string>())
            {
                if (!string.Equals(state, stateMachine.InitialState, StringComparison.Ordinal))
                    stateMachine.AddState(state);
            }

            foreach (var transition in blueprint.Transitions ?? new List<TransitionBlueprint>())
            {
                if (!stateMachine.States.Contains(transition.FromState))
                    stateMachine.AddState(transition.FromState);
                if (!stateMachine.States.Contains(transition.ToState))
                    stateMachine.AddState(transition.ToState);

                stateMachine.AddTransition(new StateTransition
                {
                    FromState = transition.FromState,
                    ToState = transition.ToState,
                    EventId = transition.EventId,
                    TriggerEventType = transition.EventId
                });
            }

            stateMachine.Publish();
            return stateMachine;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsWaitingStep(WorkflowStepDefinition? step)
    {
        if (step == null) return false;
        if (step.StepType == WorkflowStepType.HumanTask) return true;
        if (step.StepType != WorkflowStepType.Command) return false;
        return !step.NextSteps.Keys.Any(IsReservedRoute);
    }

    public static bool IsReservedRoute(string key) =>
        string.Equals(key, "Default", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, "true", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, object> AggregateEventPayloads(IReadOnlyList<DomainEvent> events)
    {
        var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var domainEvent in events.OrderBy(e => e.Timestamp))
        {
            if (domainEvent.Metadata == null || !domainEvent.Metadata.TryGetValue("Payload", out var json) || string.IsNullOrWhiteSpace(json))
                continue;

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (dict == null) continue;
                foreach (var item in dict)
                    payload[item.Key] = item.Value;
            }
            catch
            {
                // Ignore malformed payloads; kernel facts still stand.
            }
        }

        return payload;
    }

    private static Dictionary<string, object?> ToObjectDictionary(IReadOnlyDictionary<string, JsonElement>? canonicalData)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (canonicalData == null) return result;

        foreach (var item in canonicalData)
            result[item.Key] = Unwrap(item.Value);

        return result;
    }

    private static object? Unwrap(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) &&
                                      longValue.ToString(CultureInfo.InvariantCulture) == element.GetRawText()
                => longValue,
            JsonValueKind.Number => element.GetDouble(),
            _ => element.Clone()
        };
}
