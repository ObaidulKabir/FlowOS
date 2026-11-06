namespace FlowOS.Workflows.Engine;

public class WorkflowAdvanceResult
{
    public bool Success { get; }
    public string Message { get; }
    public string? NewStepId { get; }
    public string? FailureReason => !Success ? Message : null; // Added alias for better readability

    private WorkflowAdvanceResult(bool success, string message, string? newStepId)
    {
        Success = success;
        Message = message;
        NewStepId = newStepId;
    }

    public static WorkflowAdvanceResult Advanced(string newStepId) => 
        new(true, "Workflow advanced.", newStepId);

    public static WorkflowAdvanceResult StateSynchronized(string stepId, string eventType, string newState) =>
        new(true, $"Applied state-only event '{eventType}'. State is now '{newState}'.", stepId);

    public static WorkflowAdvanceResult SlaReminderFired(string stepId, string eventType) =>
        new(true, $"[SLA Reminder Fired] Reminder event '{eventType}' dispatched. Step remains '{stepId}'.", stepId);

    /// <summary>
    /// <paramref name="stepId"/> is the real step the instance landed on (set for an explicit
    /// <c>StepType: End</c> step, where <see cref="FlowOS.Workflows.Domain.WorkflowInstance.CurrentStepId"/>
    /// is that step's id) or null for the "END" sentinel transition target, which is never a real
    /// step. Callers that infer a display step id from completion (e.g.
    /// WorkflowTimeTravelService.SimulateForkAsync, which falls back to the literal string "END"
    /// only when this is null) depend on that distinction.
    /// </summary>
    public static WorkflowAdvanceResult Completed(string? stepId = null) =>
        new(true, "Workflow completed.", stepId);

    public static WorkflowAdvanceResult Failed(string reason) => 
        new(false, reason, null);
    
    public static WorkflowAdvanceResult Waiting(string reason) =>
        new(true, reason, null);
}
