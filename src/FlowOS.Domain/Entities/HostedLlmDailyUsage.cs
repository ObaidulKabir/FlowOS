namespace FlowOS.Domain.Entities;

public sealed class HostedLlmDailyUsage
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateOnly UsageDateUtc { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public int ReservedRequests { get; private set; }
    public int FinalizedRequests { get; private set; }
    public int SuccessfulRequests { get; private set; }
    public int FailedRequests { get; private set; }
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public int CountedRequests => ReservedRequests + FinalizedRequests;

    private HostedLlmDailyUsage()
    {
    }

    public HostedLlmDailyUsage(
        Guid tenantId,
        DateOnly usageDateUtc,
        string model,
        DateTime createdAtUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required.", nameof(model));

        var normalizedModel = model.Trim();
        if (normalizedModel.Length > 200)
            throw new ArgumentException("Model cannot exceed 200 characters.", nameof(model));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        UsageDateUtc = usageDateUtc;
        Model = normalizedModel;
        CreatedAtUtc = AsUtc(createdAtUtc);
        UpdatedAtUtc = CreatedAtUtc;
    }

    public bool TryReserve(int requestCount, int maximumRequests, DateTime updatedAtUtc)
    {
        if (requestCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestCount));
        if (maximumRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRequests));
        if ((long)CountedRequests + requestCount > maximumRequests)
            return false;

        ReservedRequests += requestCount;
        UpdatedAtUtc = AsUtc(updatedAtUtc);
        return true;
    }

    public void FinalizeReservation(
        int requestCount,
        bool succeeded,
        long inputTokens,
        long outputTokens,
        DateTime updatedAtUtc)
    {
        if (requestCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestCount));
        if (requestCount > ReservedRequests)
            throw new InvalidOperationException("There are not enough outstanding reservations to finalize.");
        if (inputTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(inputTokens));
        if (outputTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(outputTokens));

        ReservedRequests -= requestCount;
        FinalizedRequests += requestCount;
        if (succeeded)
            SuccessfulRequests += requestCount;
        else
            FailedRequests += requestCount;
        InputTokens += inputTokens;
        OutputTokens += outputTokens;
        UpdatedAtUtc = AsUtc(updatedAtUtc);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
