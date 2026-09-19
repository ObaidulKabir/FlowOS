using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Security.Models;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public interface IRoleRepository
{
    Task<bool> ExistsByNameAsync(Guid tenantId, string roleName, CancellationToken cancellationToken = default);
    Task<Role?> GetByNameAsync(Guid tenantId, string roleName, CancellationToken cancellationToken = default);
    Task<Role?> GetByIdAsync(Guid roleId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Role>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);
    void Add(Role role);
    void MarkPermissionsModified(Role role);

    /// <summary>Role names assigned to a user through <c>TenantUserRoles</c>, excluding the user's primary role.</summary>
    Task<IReadOnlyList<string>> ListAssignedRoleNamesAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Assigns a role to a user. Returns false when the assignment already exists.</summary>
    Task<bool> AssignToUserAsync(Guid tenantId, Guid userId, Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Revokes a role from a user. Returns false when no assignment exists.</summary>
    Task<bool> RevokeFromUserAsync(Guid tenantId, Guid userId, Guid roleId, CancellationToken cancellationToken = default);
}
