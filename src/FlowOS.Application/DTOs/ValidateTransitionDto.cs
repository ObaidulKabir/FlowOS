namespace FlowOS.Application.DTOs;

public class ValidateTransitionRequest
{
    public string EntityType { get; set; } = string.Empty;
    public string CurrentState { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
}

public class ValidateTransitionResult
{
    public bool IsAllowed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? NewState { get; set; }
    public string ResultType { get; set; } = string.Empty; // Allowed, Denied, Ignored
}
