using System;

namespace FlowOS.Core.Common.Models;

public class DeadLetterMessageDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredOnUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
    public DateTime? NextRetryUtc { get; set; }
    public bool IsDeadLetter { get; set; }
    public string? ActionType { get; set; }
    public string? TargetUrl { get; set; }
    public string? HttpMethod { get; set; }
    public string? StepId { get; set; }
}
