using FlowOS.Domain.Blueprints;
using FlowOS.Workflows.Domain;

namespace FlowOS.Application.Services;

public sealed record SlaClockJob(TimeSpan DueAfter, string EventType, string Kind);

public static class SlaSimulationClock
{
    public const string ReminderKind = "Reminder";
    public const string TimeoutKind = "Timeout";

    public static IReadOnlyList<SlaClockJob> Plan(StepSlaBlueprint? sla)
    {
        if (sla == null) return Array.Empty<SlaClockJob>();
        return Plan(
            sla.Duration,
            sla.TimeoutEvent,
            sla.Reminders?.Select(r => (r.Duration, r.TriggerEvent)));
    }

    public static IReadOnlyList<SlaClockJob> Plan(StepSlaDefinition? sla)
    {
        if (sla == null) return Array.Empty<SlaClockJob>();
        return Plan(
            sla.Duration,
            sla.TimeoutEvent,
            sla.Reminders?.Select(r => (r.Duration, r.TriggerEvent)));
    }

    public static IReadOnlyList<SlaClockJob> Plan(
        string? slaDuration,
        string? timeoutEvent,
        IEnumerable<(string Duration, string TriggerEvent)>? reminders)
    {
        var slaSpan = ParseDuration(slaDuration);
        if (slaSpan <= TimeSpan.Zero)
            slaSpan = TimeSpan.FromSeconds(1);

        var jobs = new List<SlaClockJob>();
        if (reminders != null)
        {
            foreach (var reminder in reminders)
            {
                if (string.IsNullOrWhiteSpace(reminder.Duration) || string.IsNullOrWhiteSpace(reminder.TriggerEvent))
                    continue;

                var remDur = reminder.Duration.Trim();
                var due = remDur.StartsWith("-", StringComparison.Ordinal)
                    ? slaSpan + ParseDuration(remDur)
                    : ParseDuration(remDur);
                if (due <= TimeSpan.Zero)
                    due = TimeSpan.FromSeconds(1);

                jobs.Add(new SlaClockJob(due, reminder.TriggerEvent.Trim(), ReminderKind));
            }
        }

        if (!string.IsNullOrWhiteSpace(timeoutEvent))
        {
            jobs.Add(new SlaClockJob(slaSpan, timeoutEvent.Trim(), TimeoutKind));
        }

        return jobs
            .OrderBy(job => job.DueAfter)
            .ThenBy(job => job.Kind == ReminderKind ? 0 : 1)
            .ToList();
    }

    /// <summary>
    /// Happy-path simulation: if a completing nextSteps event is already queued, fire reminders
    /// that would elapse while waiting, but do not fire the SLA timeout (the human responded in time).
    /// Overdue simulation: with autoAdvanceTimers and no completing event, fire reminders then timeout.
    /// </summary>
    public static IReadOnlyList<SlaClockJob> SelectForSimulation(
        IReadOnlyList<SlaClockJob> plan,
        bool completingEventQueued,
        bool autoAdvanceTimers,
        IReadOnlyCollection<string> alreadyQueuedEvents)
    {
        if (plan.Count == 0)
            return Array.Empty<SlaClockJob>();

        bool AlreadyQueued(string eventType) =>
            alreadyQueuedEvents.Any(queued =>
                string.Equals(queued, eventType, StringComparison.OrdinalIgnoreCase));

        IEnumerable<SlaClockJob> selected;
        if (completingEventQueued)
            selected = plan.Where(job => job.Kind == ReminderKind);
        else if (autoAdvanceTimers)
            selected = plan;
        else
            return Array.Empty<SlaClockJob>();

        return selected.Where(job => !AlreadyQueued(job.EventType)).ToList();
    }

    public static bool IsReminderEvent(StepSlaBlueprint? sla, string eventType) =>
        sla?.Reminders?.Any(r =>
            string.Equals(r.TriggerEvent, eventType, StringComparison.OrdinalIgnoreCase)) == true;

    public static bool IsReminderEvent(StepSlaDefinition? sla, string eventType) =>
        sla?.Reminders?.Any(r =>
            string.Equals(r.TriggerEvent, eventType, StringComparison.OrdinalIgnoreCase)) == true;

    public static bool IsTimeoutEvent(StepSlaBlueprint? sla, string eventType) =>
        sla != null &&
        !string.IsNullOrWhiteSpace(sla.TimeoutEvent) &&
        string.Equals(sla.TimeoutEvent, eventType, StringComparison.OrdinalIgnoreCase);

    public static bool IsTimeoutEvent(StepSlaDefinition? sla, string eventType) =>
        sla != null &&
        !string.IsNullOrWhiteSpace(sla.TimeoutEvent) &&
        string.Equals(sla.TimeoutEvent, eventType, StringComparison.OrdinalIgnoreCase);

    public static TimeSpan ParseDuration(string? durationStr)
    {
        if (string.IsNullOrWhiteSpace(durationStr))
            return TimeSpan.FromSeconds(5);

        durationStr = durationStr.Trim();
        var isNegative = durationStr.StartsWith('-');
        if (isNegative || durationStr.StartsWith('+'))
            durationStr = durationStr[1..].Trim();

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
