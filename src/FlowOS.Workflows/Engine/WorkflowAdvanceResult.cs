namespace FlowOS.Workflows.Engine;

public class WorkflowAdvanceResult
{
    public bool Success { get; }
    public string Message { get; }
    public string? NewStepId { get; }

    private WorkflowAdvanceResult(bool success, string message, string? newStepId)
    {
        Success = success;
        Message = message;
        NewStepId = newStepId;
    }

    public static WorkflowAdvanceResult Advanced(string newStepId) => 
        new(true, "Workflow advanced.", newStepId);

    public static WorkflowAdvanceResult Completed() => 
        new(true, "Workflow completed.", null);

    public static WorkflowAdvanceResult Failed(string reason) => 
        new(false, reason, null);
    
    public static WorkflowAdvanceResult Waiting(string reason) =>
        new(true, reason, null);
}
