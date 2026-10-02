using FlowOS.Domain.Entities.ExternalAI;
using FlowOS.Domain.Enums;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IExternalAgentChangeStore
{
    Task<IReadOnlyList<ExternalAgentChangeRecord>> LeaseNextAsync(
        Guid tenantId,
        string agentId,
        TimeSpan ttl,
        int limit,
        CancellationToken ct = default);

    Task AckAsync(
        Guid changeId,
        bool succeeded,
        string? error,
        CancellationToken ct = default);

    Task RenewLeaseAsync(
        Guid changeId,
        string agentId,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task<IReadOnlyList<ExternalAgentChangeRecord>> ListPendingAsync(
        Guid tenantId,
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyList<ExternalAgentChangeRecord>> ListAsync(
        Guid tenantId,
        ExternalAgentChangeStatus? status,
        int limit,
        CancellationToken ct = default);

    Task<int> ReclaimExpiredLeasesAsync(CancellationToken ct = default);

    Task AddAsync(ExternalAgentChangeRecord record, CancellationToken ct = default);

    Task<ExternalAgentChangeRecord?> GetByIdAsync(Guid changeId, CancellationToken ct = default);
}
