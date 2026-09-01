using System;

namespace FlowOS.Workflows.Domain;

public class StepSlaDefinition
{
    public string Duration { get; set; } = string.Empty;
    public string TimeoutEvent { get; set; } = string.Empty;
    public string? EscalationStepId { get; set; }
    public string? EscalationRole { get; set; }
    public bool IsInterrupting { get; set; } = true;

    public StepSlaDefinition() { }

    public StepSlaDefinition(string duration, string timeoutEvent, string? escalationStepId = null, string? escalationRole = null, bool isInterrupting = true)
    {
        Duration = duration;
        TimeoutEvent = timeoutEvent;
        EscalationStepId = escalationStepId;
        EscalationRole = escalationRole;
        IsInterrupting = isInterrupting;
    }
}
