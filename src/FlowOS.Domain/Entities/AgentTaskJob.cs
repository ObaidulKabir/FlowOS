using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Entities;

public sealed class AgentTaskJob
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public string StepId { get; private set; } = string.Empty;
    public string RequestedAgentId { get; private set; } = string.Empty;
    public string? Objective { get; private set; }
    public bool AllowAutoCommit { get; private set; }
    public bool RequireAgentActor { get; private set; }
    public AgentTaskSource Source { get; private set; }
    public AgentTaskJobStatus Status { get; private set; }
    public string? ActiveKey { get; private set; }
    public int Attempts { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime DueAtUtc { get; private set; }
    public DateTime? ClaimedAtUtc { get; private set; }
    public DateTime? ClaimExpiresAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? Claimant { get; private set; }
    public string? LastError { get; private set; }

    public bool IsTerminal =>
        Status is AgentTaskJobStatus.Completed
            or AgentTaskJobStatus.DeadLettered
            or AgentTaskJobStatus.Cancelled;

    private AgentTaskJob()
    {
    }

    public AgentTaskJob(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        string requestedAgentId,
        string? objective,
        bool allowAutoCommit,
        bool requireAgentActor,
        AgentTaskSource source,
        string activeKey,
        DateTime requestedAtUtc,
        DateTime dueAtUtc,
        int maxAttempts = 5)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (workflowInstanceId == Guid.Empty)
            throw new ArgumentException("WorkflowInstanceId is required.", nameof(workflowInstanceId));
        if (string.IsNullOrWhiteSpace(stepId))
            throw new ArgumentException("StepId is required.", nameof(stepId));
        if (string.IsNullOrWhiteSpace(requestedAgentId))
            throw new ArgumentException("RequestedAgentId is required.", nameof(requestedAgentId));
        if (string.IsNullOrWhiteSpace(activeKey))
            throw new ArgumentException("ActiveKey is required for an active job.", nameof(activeKey));
        if (maxAttempts is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be between 1 and 100.");

        Id = Guid.NewGuid();
        TenantId = tenantId;
        WorkflowInstanceId = workflowInstanceId;
        StepId = RequiredTrimmed(stepId, 200, nameof(stepId));
        RequestedAgentId = RequiredTrimmed(requestedAgentId, 200, nameof(requestedAgentId));
        Objective = TrimmedOrNull(objective, 2000);
        AllowAutoCommit = allowAutoCommit;
        RequireAgentActor = requireAgentActor;
        Source = source;
        Status = AgentTaskJobStatus.Pending;
        ActiveKey = RequiredTrimmed(activeKey, 450, nameof(activeKey));
        Attempts = 0;
        MaxAttempts = maxAttempts;
        RequestedAtUtc = AsUtc(requestedAtUtc);
        DueAtUtc = AsUtc(dueAtUtc);
    }

    public static string CreateActiveKey(Guid tenantId, Guid workflowInstanceId, string stepId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (workflowInstanceId == Guid.Empty)
            throw new ArgumentException("WorkflowInstanceId is required.", nameof(workflowInstanceId));
        if (string.IsNullOrWhiteSpace(stepId))
            throw new ArgumentException("StepId is required.", nameof(stepId));

        return $"agent-task:{tenantId:N}:{workflowInstanceId:N}:{stepId.Trim().ToLowerInvariant()}";
    }

    public void Claim(string claimant, DateTime claimedAtUtc, DateTime claimExpiresAtUtc)
    {
        var claimedAt = AsUtc(claimedAtUtc);
        var expiresAt = AsUtc(claimExpiresAtUtc);

        if (Status is not (AgentTaskJobStatus.Pending or AgentTaskJobStatus.RetryScheduled))
            throw new InvalidOperationException($"A job in status '{Status}' cannot be claimed.");
        if (DueAtUtc > claimedAt)
            throw new InvalidOperationException("The job is not due yet.");
        if (expiresAt <= claimedAt)
            throw new ArgumentOutOfRangeException(nameof(claimExpiresAtUtc), "Claim expiry must be after claim time.");

        Status = AgentTaskJobStatus.Claimed;
        Claimant = RequiredTrimmed(claimant, 200, nameof(claimant));
        ClaimedAtUtc = claimedAt;
        ClaimExpiresAtUtc = expiresAt;
        Attempts++;
    }

    public void Complete(string claimant, DateTime completedAtUtc)
    {
        EnsureClaimedBy(claimant);
        MarkTerminal(AgentTaskJobStatus.Completed, completedAtUtc, null);
    }

    public void Retry(string claimant, string sanitizedError, DateTime dueAtUtc, DateTime changedAtUtc)
    {
        EnsureClaimedBy(claimant);

        if (Attempts >= MaxAttempts)
        {
            MarkTerminal(AgentTaskJobStatus.DeadLettered, changedAtUtc, sanitizedError);
            return;
        }

        Status = AgentTaskJobStatus.RetryScheduled;
        DueAtUtc = AsUtc(dueAtUtc);
        LastError = TrimmedOrNull(sanitizedError, 2000);
        ClearClaim();
    }

    public void DeadLetter(string claimant, string sanitizedError, DateTime completedAtUtc)
    {
        EnsureClaimedBy(claimant);
        MarkTerminal(AgentTaskJobStatus.DeadLettered, completedAtUtc, sanitizedError);
    }

    public void Cancel(string? sanitizedReason, DateTime completedAtUtc)
    {
        if (IsTerminal)
            return;

        MarkTerminal(AgentTaskJobStatus.Cancelled, completedAtUtc, sanitizedReason);
    }

    public bool ReconcileExpiredClaim(DateTime nowUtc, string sanitizedError)
    {
        var now = AsUtc(nowUtc);
        if (Status != AgentTaskJobStatus.Claimed ||
            ClaimExpiresAtUtc == null ||
            ClaimExpiresAtUtc > now)
        {
            return false;
        }

        if (Attempts >= MaxAttempts)
        {
            MarkTerminal(AgentTaskJobStatus.DeadLettered, now, sanitizedError);
        }
        else
        {
            Status = AgentTaskJobStatus.RetryScheduled;
            DueAtUtc = now;
            LastError = TrimmedOrNull(sanitizedError, 2000);
            ClearClaim();
        }

        return true;
    }

    private void MarkTerminal(AgentTaskJobStatus status, DateTime completedAtUtc, string? error)
    {
        Status = status;
        CompletedAtUtc = AsUtc(completedAtUtc);
        LastError = TrimmedOrNull(error, 2000);
        ActiveKey = null;
        ClearClaim();
    }

    private void EnsureClaimedBy(string claimant)
    {
        if (Status != AgentTaskJobStatus.Claimed)
            throw new InvalidOperationException($"A job in status '{Status}' is not claimed.");
        if (!string.Equals(Claimant, claimant?.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("The job is claimed by a different worker.");
    }

    private void ClearClaim()
    {
        Claimant = null;
        ClaimedAtUtc = null;
        ClaimExpiresAtUtc = null;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static string RequiredTrimmed(string value, int maxLength, string parameterName)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Value is required.", parameterName);
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        return trimmed;
    }

    private static string? TrimmedOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
