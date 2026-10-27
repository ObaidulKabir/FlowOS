using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Domain.Blueprints;

namespace FlowOS.Domain.Services;

/// <summary>
/// CR-16: static "Law" check. Finds workflow exits that the state machine can never allow, so the step
/// would be stuck at runtime (class-backed instances fail closed on a state-machine violation).
/// <para>
/// Mirrors the runtime rules: an event the state machine does not mention is ignored (the step moves, the
/// state stays); a known event with no transition from the current state is denied; an allowed event that
/// is not a step exit changes only the state; a Decision routes without an event. Reachable states are
/// over-approximated (all guarded targets, state-only changes, and every reachable state after a Fork), so
/// a reported exit is impossible in every state the entity can be in at that step: no false alarms.
/// </para>
/// </summary>
public static class WorkflowLawAnalyzer
{
    public sealed record DeadExit(string StepId, string EventId, IReadOnlyCollection<string> StatesAtStep, IReadOnlyCollection<string> AllowedFromStates);

    private const string End = "END";

    public static IReadOnlyList<DeadExit> FindDeadExits(WorkflowClassBlueprint blueprint)
    {
        var sm = blueprint.StateMachine;
        var workflow = blueprint.Workflow;
        if (sm == null || workflow == null || sm.Transitions == null || sm.Transitions.Count == 0
            || string.IsNullOrWhiteSpace(sm.InitialState) || string.IsNullOrWhiteSpace(workflow.StartStepId))
            return Array.Empty<DeadExit>();

        var steps = (workflow.Steps ?? new List<StepBlueprint>())
            .Where(s => !string.IsNullOrWhiteSpace(s.StepId))
            .GroupBy(s => s.StepId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (!steps.ContainsKey(workflow.StartStepId)) return Array.Empty<DeadExit>();

        var knownEvents = sm.Transitions.Select(t => t.EventId).ToHashSet(StringComparer.Ordinal);
        var statesAtStep = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var forkSeeds = new HashSet<string>(StringComparer.Ordinal);

        // 1. Joint walk over (step, state) until a Fork.
        var seen = new HashSet<(string Step, string State)>();
        var queue = new Queue<(string Step, string State)>();
        void Visit(string step, string state)
        {
            if (string.Equals(step, End, StringComparison.OrdinalIgnoreCase) || !steps.TryGetValue(step, out var target)) return;
            if (IsFork(target)) { forkSeeds.Add(step); return; }
            if (seen.Add((step, state))) queue.Enqueue((step, state));
        }
        Visit(workflow.StartStepId, sm.InitialState);

        while (queue.Count > 0)
        {
            var (stepId, state) = queue.Dequeue();
            var step = steps[stepId];
            Add(statesAtStep, stepId, state);
            var exits = Exits(step);
            var exitEvents = exits.Select(e => e.EventId).ToHashSet(StringComparer.Ordinal);

            // An allowed event that is not an exit of this step changes only the state.
            foreach (var t in sm.Transitions.Where(t => t.FromState == state && !exitEvents.Contains(t.EventId)))
                Visit(stepId, t.ToState);

            foreach (var (eventId, targetStep) in exits)
            {
                if (!knownEvents.Contains(eventId))
                {
                    Visit(targetStep, state); // ignored by the state machine
                    continue;
                }
                foreach (var t in sm.Transitions.Where(t => t.FromState == state && t.EventId == eventId))
                    Visit(targetStep, t.ToState);
            }

            // A Decision routes to its targets without a state-machine event.
            foreach (var target in Conditions(step).Values)
                Visit(target, state);
        }

        // 2. After a Fork, branches share the state, so assume any state the machine can reach.
        if (forkSeeds.Count > 0)
        {
            var anyReachable = ReachableStates(sm);
            var stack = new Stack<string>(forkSeeds);
            var global = new HashSet<string>(StringComparer.Ordinal);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (string.Equals(id, End, StringComparison.OrdinalIgnoreCase) || !steps.TryGetValue(id, out var step) || !global.Add(id)) continue;
                foreach (var s in anyReachable) Add(statesAtStep, id, s);
                foreach (var next in (step.Branches ?? new List<string>()).Concat(NextSteps(step).Values).Concat(Conditions(step).Values))
                    stack.Push(next);
                if (!string.IsNullOrWhiteSpace(step.Sla?.EscalationStepId)) stack.Push(step.Sla!.EscalationStepId!);
            }
        }

        // 3. An exit on a known event must be allowed from at least one state the entity can be in there.
        var dead = new List<DeadExit>();
        foreach (var (stepId, states) in statesAtStep.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            foreach (var (eventId, _) in Exits(steps[stepId]).DistinctBy(e => e.EventId))
            {
                if (!knownEvents.Contains(eventId)) continue;
                var allowedFrom = sm.Transitions.Where(t => t.EventId == eventId).Select(t => t.FromState).Distinct(StringComparer.Ordinal).ToList();
                if (!allowedFrom.Any(states.Contains))
                    dead.Add(new DeadExit(stepId, eventId, states.OrderBy(s => s, StringComparer.Ordinal).ToList(), allowedFrom));
            }
        }
        return dead;
    }

    private static bool IsFork(StepBlueprint step) =>
        string.Equals(step.StepType, "Fork", StringComparison.OrdinalIgnoreCase) || (step.Branches?.Count ?? 0) > 0;

    private static List<(string EventId, string Target)> Exits(StepBlueprint step)
    {
        var exits = NextSteps(step).Select(kv => (kv.Key, kv.Value)).ToList();
        var timeout = step.Sla?.TimeoutEvent;
        if (!string.IsNullOrWhiteSpace(timeout) && !string.IsNullOrWhiteSpace(step.Sla!.EscalationStepId)
            && !NextSteps(step).ContainsKey(timeout))
            exits.Add((timeout, step.Sla.EscalationStepId!));
        return exits;
    }

    private static IReadOnlyDictionary<string, string> NextSteps(StepBlueprint step) =>
        step.NextSteps ?? new Dictionary<string, string>();

    private static IReadOnlyDictionary<string, string> Conditions(StepBlueprint step) =>
        step.Conditions ?? new Dictionary<string, string>();

    private static HashSet<string> ReachableStates(StateMachineBlueprint sm)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal) { sm.InitialState };
        var stack = new Stack<string>(reached);
        while (stack.Count > 0)
        {
            var from = stack.Pop();
            foreach (var t in sm.Transitions.Where(t => t.FromState == from))
                if (reached.Add(t.ToState)) stack.Push(t.ToState);
        }
        return reached;
    }

    private static void Add(Dictionary<string, HashSet<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var set)) map[key] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(value);
    }
}
