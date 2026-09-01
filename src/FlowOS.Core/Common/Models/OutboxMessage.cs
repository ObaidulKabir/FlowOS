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

    protected OutboxMessage() { }

    public OutboxMessage(Guid tenantId, string type, string payload)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        Type = type;
        Payload = payload;
        OccurredOnUtc = DateTime.UtcNow;
        ProcessedOnUtc = null;
        Error = null;
        RetryCount = 0;
    }

    public void MarkAsProcessed()
    {
        ProcessedOnUtc = DateTime.UtcNow;
        Error = null;
    }

    public void RecordFailure(string error)
    {
        Error = error;
        RetryCount++;
    }
}
