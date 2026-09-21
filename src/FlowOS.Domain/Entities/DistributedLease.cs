namespace FlowOS.Domain.Entities;

public sealed class DistributedLease
{
    public string LeaseKey { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public DateTime AcquiredAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    private DistributedLease()
    {
    }

    public DistributedLease(
        string leaseKey,
        string ownerId,
        DateTime acquiredAtUtc,
        DateTime expiresAtUtc)
    {
        LeaseKey = Required(leaseKey, 300, nameof(leaseKey));
        OwnerId = Required(ownerId, 200, nameof(ownerId));
        AcquiredAtUtc = AsUtc(acquiredAtUtc);
        ExpiresAtUtc = AsUtc(expiresAtUtc);

        if (ExpiresAtUtc <= AcquiredAtUtc)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Lease expiry must be after acquisition.");
    }

    public bool TryAcquire(string ownerId, DateTime acquiredAtUtc, DateTime expiresAtUtc)
    {
        var acquiredAt = AsUtc(acquiredAtUtc);
        var expiresAt = AsUtc(expiresAtUtc);
        var normalizedOwner = Required(ownerId, 200, nameof(ownerId));

        if (expiresAt <= acquiredAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Lease expiry must be after acquisition.");
        if (ExpiresAtUtc > acquiredAt)
            return false;

        OwnerId = normalizedOwner;
        AcquiredAtUtc = acquiredAt;
        ExpiresAtUtc = expiresAt;
        return true;
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

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
