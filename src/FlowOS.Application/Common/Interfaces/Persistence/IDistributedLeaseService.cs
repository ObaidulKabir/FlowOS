namespace FlowOS.Application.Common.Interfaces.Persistence;

public sealed record DistributedLeaseHandle(
    string LeaseKey,
    string OwnerId,
    DateTime AcquiredAtUtc,
    DateTime ExpiresAtUtc);

public interface IDistributedLeaseService
{
    Task<DistributedLeaseHandle?> TryAcquireAsync(
        string leaseKey,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(
        string leaseKey,
        string ownerId,
        CancellationToken cancellationToken = default);
}
