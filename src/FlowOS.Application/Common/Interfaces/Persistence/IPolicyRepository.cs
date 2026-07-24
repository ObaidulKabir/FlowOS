using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Security.Models;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IPolicyRepository
{
    Task<bool> ExistsByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken = default);
    Task<Policy?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
    void Add(Policy policy);
}
