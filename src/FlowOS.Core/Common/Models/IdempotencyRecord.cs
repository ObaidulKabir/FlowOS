using System;

namespace FlowOS.Core.Common.Models;

public class IdempotencyRecord
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string OperationName { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Pending";
    public string? ResultJson { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    private IdempotencyRecord()
    {
    }

    public IdempotencyRecord(Guid tenantId, string operationName, string idempotencyKey)
    {
        TenantId = tenantId;
        OperationName = operationName?.Trim() ?? string.Empty;
        IdempotencyKey = idempotencyKey?.Trim() ?? string.Empty;
    }

    public void MarkCompleted(string resultJson)
    {
        Status = "Completed";
        ResultJson = resultJson;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkPending()
    {
        Status = "Pending";
        ResultJson = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string? resultJson = null)
    {
        Status = "Failed";
        ResultJson = resultJson;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
