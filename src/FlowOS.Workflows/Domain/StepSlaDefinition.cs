using System;
using System.Collections.Generic;

namespace FlowOS.Workflows.Domain;

public class StepSlaDefinition
{
    public string Duration { get; set; } = string.Empty;
    public string TimeoutEvent { get; set; } = string.Empty;
    public string? EscalationStepId { get; set; }
    public string? EscalationRole { get; set; }
    public bool IsInterrupting { get; set; } = true;
    public List<StepReminderDefinition> Reminders { get; set; } = new();

    public StepSlaDefinition() { }

    public StepSlaDefinition(
        string duration,
        string timeoutEvent,
        string? escalationStepId = null,
        string? escalationRole = null,
        bool isInterrupting = true,
        List<StepReminderDefinition>? reminders = null)
    {
        Duration = duration;
        TimeoutEvent = timeoutEvent;
        EscalationStepId = escalationStepId;
        EscalationRole = escalationRole;
        IsInterrupting = isInterrupting;
        if (reminders != null) Reminders = reminders;
    }
}

public class StepReminderDefinition
{
    public string Duration { get; set; } = string.Empty;
    public string TriggerEvent { get; set; } = string.Empty;

    public StepReminderDefinition() { }

    public StepReminderDefinition(string duration, string triggerEvent)
    {
        Duration = duration;
        TriggerEvent = triggerEvent;
    }
}
