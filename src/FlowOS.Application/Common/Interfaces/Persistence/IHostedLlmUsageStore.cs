namespace FlowOS.Application.Common.Interfaces.Persistence;

public sealed record HostedLlmUsageSnapshot(
    Guid TenantId,
    DateOnly UsageDateUtc,
    string Model,
    int ReservedRequests,
    int FinalizedRequests,
    int SuccessfulRequests,
    int FailedRequests,
    long InputTokens,
    long OutputTokens,
    DateTime UpdatedAtUtc);

public sealed record HostedLlmUsageReservation(
    bool Allowed,
    int MaximumRequests,
    HostedLlmUsageSnapshot? Usage)
{
    public int RemainingRequests =>
        Usage == null
            ? MaximumRequests
            : Math.Max(0, MaximumRequests - Usage.ReservedRequests - Usage.FinalizedRequests);
}

public interface IHostedLlmUsageStore
{
    Task<HostedLlmUsageReservation> TryReserveAsync(
        Guid tenantId,
        DateOnly usageDateUtc,
        string model,
        int maximumRequests,
        int requestCount = 1,
        CancellationToken cancellationToken = default);

    Task<HostedLlmUsageSnapshot> FinalizeAsync(
        Guid tenantId,
        DateOnly usageDateUtc,
        string model,
        bool succeeded,
        long inputTokens = 0,
        long outputTokens = 0,
        int requestCount = 1,
        CancellationToken cancellationToken = default);

    Task<HostedLlmUsageSnapshot?> ReadAsync(
        Guid tenantId,
        DateOnly usageDateUtc,
        string model,
        CancellationToken cancellationToken = default);
}
