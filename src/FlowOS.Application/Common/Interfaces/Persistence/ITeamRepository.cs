using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface ITeamRepository
{
    Task<Team?> GetByIdAsync(Guid tenantId, Guid teamId, CancellationToken cancellationToken = default);
    Task<List<Team>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<List<Team>> ListByCapabilitiesAsync(Guid tenantId, IReadOnlyCollection<string> requiredCapabilities, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Evaluates if the given user is a member of any team that possesses the required capabilities,
    /// at or above the required escalation level order.
    /// </summary>
    Task<bool> IsUserAuthorizedForCapabilitiesAsync(
        Guid tenantId,
        Guid tenantUserId,
        IReadOnlyCollection<string> requiredCapabilities,
        int minHierarchyOrder = 0,
        CancellationToken cancellationToken = default);

    void Add(Team team);
    void Remove(Team team);
}
