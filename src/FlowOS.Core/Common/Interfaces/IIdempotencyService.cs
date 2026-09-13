using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Core.Common.Interfaces;

public record IdempotencyStatusDto(
    Guid TenantId,
    string OperationName,
    string IdempotencyKey,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public interface IIdempotencyService
{
    Task<bool> TryBeginAsync(
        Guid tenantId,
        string operationName,
        string idempotencyKey,
        CancellationToken ct = default);

    Task CompleteAsync<T>(
        Guid tenantId,
        string operationName,
        string idempotencyKey,
        T result,
        CancellationToken ct = default);

    Task FailAsync(
        Guid tenantId,
        string operationName,
        string idempotencyKey,
        string? reason = null,
        CancellationToken ct = default);

    Task<(bool Found, T? Result)> TryGetCompletedResultAsync<T>(
        Guid tenantId,
        string operationName,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<IdempotencyStatusDto?> GetStatusAsync(
        Guid tenantId,
        string operationName,
        string idempotencyKey,
        CancellationToken ct = default);
}
