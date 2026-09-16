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
