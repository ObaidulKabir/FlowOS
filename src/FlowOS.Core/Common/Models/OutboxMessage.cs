using System;

namespace FlowOS.Core.Common.Models;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTime OccurredOnUtc { get; private set; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public string? Error { get; private set; }
    public int RetryCount { get; private set; }

    public DateTime? NextRetryUtc { get; private set; }
    public bool IsDeadLetter { get; private set; }
    public int MaxRetries { get; private set; } = 5;

    protected OutboxMessage() { }

    public OutboxMessage(Guid tenantId, string type, string payload, int maxRetries = 5)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        Type = type;
        Payload = payload;
        OccurredOnUtc = DateTime.UtcNow;
        ProcessedOnUtc = null;
        Error = null;
        RetryCount = 0;
        NextRetryUtc = null;
        IsDeadLetter = false;
        MaxRetries = maxRetries > 0 ? maxRetries : 5;
    }

    public void MarkAsProcessed()
    {
        ProcessedOnUtc = DateTime.UtcNow;
        NextRetryUtc = null;
        IsDeadLetter = false;
        Error = null;
    }

    public void RecordFailure(string error, int? maxRetries = null, int baseDelaySeconds = 2)
    {
        Error = error;
        RetryCount++;
        var limit = maxRetries ?? MaxRetries;
        if (RetryCount >= limit)
        {
            IsDeadLetter = true;
            NextRetryUtc = null;
        }
        else
        {
            // Exponential backoff: baseDelay * 2^(RetryCount - 1)
            var delaySeconds = Math.Min(3600, (int)Math.Pow(2, RetryCount - 1) * Math.Max(1, baseDelaySeconds));
            NextRetryUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
        }
    }

    public void ReplayFromDeadLetter()
    {
        IsDeadLetter = false;
        RetryCount = 0;
        NextRetryUtc = DateTime.UtcNow;
        Error = null;
        ProcessedOnUtc = null;
    }
}
