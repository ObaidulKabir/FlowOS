using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowOS.Agents.Abstractions;

public static class AgentSuggestionContract
{
    public static AgentResult RestrictToLegalEvents(
        AgentResult result,
        IReadOnlyCollection<string> legalEvents)
    {
        if (result.SuggestedActions.Count == 0)
            return result;

        var allowed = new HashSet<string>(legalEvents, StringComparer.OrdinalIgnoreCase);
        var filtered = result.SuggestedActions
            .Where(action => allowed.Contains(action.EventType))
            .ToList();

        if (filtered.Count == result.SuggestedActions.Count)
            return result;

        result.SuggestedActions = filtered;
        if (filtered.Count == 0 && string.IsNullOrWhiteSpace(result.Insight) == false)
        {
            result.Insight = $"{result.Insight} Suggested event was outside the legal nextSteps set and was dropped.";
        }

        return result;
    }
}

public enum AgentDecisionKind
{
    Skipped,
    Commit,
    Park
}

public sealed record AgentSimulationSuggestion(
    string? Event,
    double Confidence = 1.0,
    string AgentId = AgentDecisionPolicy.DefaultAgentId);

public sealed record AgentDecisionEvaluation(
    AgentDecisionKind Kind,
    SuggestedAction? Candidate,
    string Reason)
{
    public bool ShouldCommit => Kind == AgentDecisionKind.Commit;
    public bool IsParked => Kind == AgentDecisionKind.Park;
    public string? ParkReason => IsParked ? Reason : null;
}

public sealed record AgentDecisionEnvelope(
    string AgentId,
    AgentResult Result,
    AgentDecisionEvaluation Evaluation,
    SuggestedAction? OriginalCandidate = null)
{
    public SuggestedAction? Candidate => Evaluation.Candidate;
}

/// <summary>
/// Shared bounded-autonomy policy used by live execution and deterministic simulation.
/// Provider execution stays outside this class; it only filters and evaluates suggestions.
/// </summary>
public static class AgentDecisionPolicy
{
    public const string DefaultAgentId = "RiskAnalysisAgent";

    public static AgentDecisionEnvelope Evaluate(
        DecisionPacket packet,
        AgentResult result,
        string? agentId = null)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(result);

        var originalCandidate = HighestConfidence(result.SuggestedActions);
        var filtered = AgentSuggestionContract.RestrictToLegalEvents(
            result,
            packet.LegalNextStepEvents);
        var candidate = HighestConfidence(filtered.SuggestedActions);
        var resolvedAgentId = NormalizeAgentId(agentId);

        if (candidate == null)
        {
            return new AgentDecisionEnvelope(
                resolvedAgentId,
                filtered,
                new AgentDecisionEvaluation(
                    AgentDecisionKind.Park,
                    null,
                    "No legal suggestion."),
                originalCandidate);
        }

        var canCommit = AutoCommitEvaluator.CanCommit(packet, candidate, out var reason);
        return new AgentDecisionEnvelope(
            resolvedAgentId,
            filtered,
            new AgentDecisionEvaluation(
                canCommit ? AgentDecisionKind.Commit : AgentDecisionKind.Park,
                candidate,
                reason),
            originalCandidate);
    }

    public static AgentDecisionEnvelope EvaluateSimulation(
        DecisionPacket packet,
        bool autoAdvanceAgents,
        AgentSimulationSuggestion? simulatedAgent = null)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var agentId = NormalizeAgentId(simulatedAgent?.AgentId);
        if (!StepActorIsAgentHandled(packet.Actor))
        {
            return Skipped(
                agentId,
                $"Step actor '{packet.Actor}' does not allow agent handling.");
        }

        if (!autoAdvanceAgents && simulatedAgent == null)
        {
            return Skipped(agentId, "Agent auto-advance is disabled.");
        }

        var suggestedEvent = string.IsNullOrWhiteSpace(simulatedAgent?.Event)
            ? packet.AutoCommit?.AllowedEvents.FirstOrDefault()
            : simulatedAgent.Event.Trim();
        var confidence = Math.Clamp(simulatedAgent?.Confidence ?? 1.0, 0.0, 1.0);
        var result = new AgentResult
        {
            Success = true,
            Insight = "Deterministic simulation suggestion.",
            SuggestedActions = string.IsNullOrWhiteSpace(suggestedEvent)
                ? new List<SuggestedAction>()
                :
                [
                    new SuggestedAction(
                        suggestedEvent,
                        "Simulated agent suggestion.",
                        confidence)
                ]
        };

        return Evaluate(packet, result, agentId);
    }

    private static AgentDecisionEnvelope Skipped(string agentId, string reason) =>
        new(
            agentId,
            new AgentResult
            {
                Success = true,
                Insight = reason
            },
            new AgentDecisionEvaluation(AgentDecisionKind.Skipped, null, reason));

    private static SuggestedAction? HighestConfidence(IEnumerable<SuggestedAction> actions) =>
        actions
            .Select((action, index) => new { Action = action, Index = index })
            .OrderByDescending(item => item.Action.Confidence)
            .ThenBy(item => item.Index)
            .Select(item => item.Action)
            .FirstOrDefault();

    private static bool StepActorIsAgentHandled(string actor) =>
        string.Equals(actor, "Agent", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(actor, "Either", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAgentId(string? agentId) =>
        string.IsNullOrWhiteSpace(agentId) ? DefaultAgentId : agentId.Trim();
}

public static class AutoCommitEvaluator
{
    public static bool CanCommit(DecisionPacket packet, SuggestedAction action, out string reason)
    {
        if (packet.AutoCommit == null)
        {
            reason = "No autoCommit policy on the current step.";
            return false;
        }

        if (!StepActorIsAgentHandled(packet.Actor))
        {
            reason = $"Step actor '{packet.Actor}' does not allow auto-commit.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(action.EventType))
        {
            reason = "Suggested action has no event type.";
            return false;
        }

        if (IsReservedRoute(action.EventType))
        {
            reason = "Default/true routes cannot be auto-committed.";
            return false;
        }

        if (IsTimeoutOrReminder(packet, action.EventType))
        {
            reason = "TimeoutEvent and SLA reminder events stay timer-owned.";
            return false;
        }

        if (!ContainsInsensitive(packet.LegalNextStepEvents, action.EventType))
        {
            reason = $"Event '{action.EventType}' is not a legal nextSteps key.";
            return false;
        }

        if (packet.AutoCommit.AllowedEvents.Count == 0)
        {
            reason = "autoCommit.allowedEvents is empty.";
            return false;
        }

        if (!ContainsInsensitive(packet.AutoCommit.AllowedEvents, action.EventType))
        {
            reason = $"Event '{action.EventType}' is not in autoCommit.allowedEvents.";
            return false;
        }

        if (action.Confidence < packet.AutoCommit.MinConfidence)
        {
            reason = $"Confidence {action.Confidence:0.00} is below minConfidence {packet.AutoCommit.MinConfidence:0.00}.";
            return false;
        }

        reason = "In bounds.";
        return true;
    }

    private static bool StepActorIsAgentHandled(string actor) =>
        string.Equals(actor, "Agent", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(actor, "Either", StringComparison.OrdinalIgnoreCase);

    private static bool IsReservedRoute(string eventType) =>
        string.Equals(eventType, "Default", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsTimeoutOrReminder(DecisionPacket packet, string eventType)
    {
        if (!string.IsNullOrWhiteSpace(packet.TimeoutEvent) &&
            string.Equals(packet.TimeoutEvent, eventType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return packet.SlaReminders.Any(reminder =>
            string.Equals(reminder.TriggerEvent, eventType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsInsensitive(IEnumerable<string> values, string candidate) =>
        values.Any(value => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
}
