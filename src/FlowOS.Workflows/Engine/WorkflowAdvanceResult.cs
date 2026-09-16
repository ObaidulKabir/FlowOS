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

    public static WorkflowAdvanceResult Completed() => 
        new(true, "Workflow completed.", null);

    public static WorkflowAdvanceResult Failed(string reason) => 
        new(false, reason, null);
    
    public static WorkflowAdvanceResult Waiting(string reason) =>
        new(true, reason, null);
}
