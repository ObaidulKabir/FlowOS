using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Security.Models;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IRoleRepository
{
    Task<bool> ExistsByNameAsync(Guid tenantId, string roleName, CancellationToken cancellationToken = default);
    Task<Role?> GetByIdAsync(Guid roleId, Guid tenantId, CancellationToken cancellationToken = default);
    void Add(Role role);
    void MarkPermissionsModified(Role role);
}
