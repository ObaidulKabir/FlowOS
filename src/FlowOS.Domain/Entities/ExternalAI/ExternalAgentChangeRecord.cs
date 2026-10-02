using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Entities.ExternalAI;

public sealed class ExternalAgentChangeRecord
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? SourceOutboxMessageId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public ExternalAgentChangeStatus Status { get; private set; }
    public string? LeasedByAgent { get; private set; }
    public DateTime? LeasedUntilUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; } = 5;
    public DateTime? NextRetryUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }

    private ExternalAgentChangeRecord()
    {
    }

    public static ExternalAgentChangeRecord FromOutboxMessage(
        Guid id,
        Guid tenantId,
        Guid sourceOutboxMessageId,
        string eventType,
        string payloadJson,
        DateTime createdAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (sourceOutboxMessageId == Guid.Empty)
            throw new ArgumentException("SourceOutboxMessageId is required.", nameof(sourceOutboxMessageId));

        return new ExternalAgentChangeRecord
        {
            Id = id,
            TenantId = tenantId,
            SourceOutboxMessageId = sourceOutboxMessageId,
            EventType = Required(eventType, 200, nameof(eventType)),
            PayloadJson = Required(payloadJson, nameof(payloadJson)),
            Status = ExternalAgentChangeStatus.Pending,
            LeasedByAgent = null,
            LeasedUntilUtc = null,
            AttemptCount = 0,
            MaxAttempts = 5,
            NextRetryUtc = null,
            LastError = null,
            CreatedAtUtc = AsUtc(createdAtUtc),
            ProcessedAtUtc = null
        };
    }

    public static ExternalAgentChangeRecord Create(
        Guid id,
        Guid tenantId,
        Guid? sourceOutboxMessageId,
        string eventType,
        string payloadJson,
        DateTime createdAtUtc,
        int maxAttempts = 5)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Id is required.", nameof(id));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));

        return new ExternalAgentChangeRecord
        {
            Id = id,
            TenantId = tenantId,
            SourceOutboxMessageId = sourceOutboxMessageId,
            EventType = Required(eventType, 200, nameof(eventType)),
            PayloadJson = Required(payloadJson, nameof(payloadJson)),
            Status = ExternalAgentChangeStatus.Pending,
            LeasedByAgent = null,
            LeasedUntilUtc = null,
            AttemptCount = 0,
            MaxAttempts = maxAttempts > 0 ? maxAttempts : 5,
            NextRetryUtc = null,
            LastError = null,
            CreatedAtUtc = AsUtc(createdAtUtc),
            ProcessedAtUtc = null
        };
    }

    public void MarkLeased(string agentId, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("AgentId is required.", nameof(agentId));
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        if (Status != ExternalAgentChangeStatus.Pending && Status != ExternalAgentChangeStatus.Failed)
            throw new InvalidOperationException($"Cannot lease change in status {Status}.");

        Status = ExternalAgentChangeStatus.Leased;
        LeasedByAgent = Required(agentId, 200, nameof(agentId));
        LeasedUntilUtc = DateTime.UtcNow.Add(ttl);
    }

    public void RenewLease(TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        if (Status != ExternalAgentChangeStatus.Leased)
            throw new InvalidOperationException($"Cannot renew lease on change in status {Status}.");
        if (string.IsNullOrWhiteSpace(LeasedByAgent))
            throw new InvalidOperationException("Cannot renew a lease without a lease owner.");

        LeasedUntilUtc = DateTime.UtcNow.Add(ttl);
    }

    public void MarkProcessed()
    {
        if (Status != ExternalAgentChangeStatus.Leased)
            throw new InvalidOperationException($"Cannot process change in status {Status}.");

        Status = ExternalAgentChangeStatus.Processed;
        ProcessedAtUtc = DateTime.UtcNow;
        LeasedByAgent = null;
        LeasedUntilUtc = null;
        NextRetryUtc = null;
        LastError = null;
    }

    public void MarkFailed(string error, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(error))
            throw new ArgumentException("Error is required.", nameof(error));
        if (Status != ExternalAgentChangeStatus.Leased)
            throw new InvalidOperationException($"Cannot mark failed change in status {Status}.");

        AttemptCount++;
        LastError = TrimTo(error, 4000);

        if (AttemptCount >= MaxAttempts)
        {
            Status = ExternalAgentChangeStatus.DeadLetter;
            LeasedByAgent = null;
            LeasedUntilUtc = null;
            NextRetryUtc = null;
        }
        else
        {
            Status = ExternalAgentChangeStatus.Failed;
            LeasedByAgent = null;
            LeasedUntilUtc = null;
            NextRetryUtc = ComputeNextRetryUtc(AttemptCount, now);
        }
    }

    public void ReclaimLease()
    {
        if (Status != ExternalAgentChangeStatus.Leased)
            throw new InvalidOperationException($"Cannot reclaim lease on change in status {Status}.");

        Status = ExternalAgentChangeStatus.Pending;
        LeasedByAgent = null;
        LeasedUntilUtc = null;
    }

    public DateTime ComputeNextRetryUtc(int attempt, DateTime now)
    {
        var delaySeconds = Math.Min(3600, (int)Math.Pow(2, attempt - 1) * 2);
        return AsUtc(now).AddSeconds(delaySeconds);
    }

    private static string Required(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        return trimmed;
    }

    private static string Required(string value, string parameterName)
    {
        if (value == null)
            throw new ArgumentNullException(parameterName);
        return value;
    }

    private static string? TrimTo(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
