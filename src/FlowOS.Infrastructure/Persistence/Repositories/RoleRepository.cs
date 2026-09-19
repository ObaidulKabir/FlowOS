using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly FlowOSDbContext _context;

    public RoleRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public Task<bool> ExistsByNameAsync(Guid tenantId, string roleName, CancellationToken cancellationToken = default)
        => _context.Roles.AnyAsync(r => r.TenantId == tenantId && r.Name == roleName, cancellationToken);

    public Task<Role?> GetByNameAsync(Guid tenantId, string roleName, CancellationToken cancellationToken = default)
        => _context.Roles.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == roleName, cancellationToken);

    public Task<Role?> GetByIdAsync(Guid roleId, Guid tenantId, CancellationToken cancellationToken = default)
        => _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId && r.TenantId == tenantId, cancellationToken);

    public Task<Role?> GetByNameAsync(Guid tenantId, string roleName, CancellationToken cancellationToken = default)
        => _context.Roles.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == roleName, cancellationToken);

    public async Task<IReadOnlyList<Role>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await _context.Roles
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

    public void Add(Role role) => _context.Roles.Add(role);

    public void MarkPermissionsModified(Role role)
        => _context.Entry(role).Property(r => r.Permissions).IsModified = true;

    public async Task<IReadOnlyList<string>> ListAssignedRoleNamesAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => await _context.TenantUserRoles
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.TenantUserId == userId)
            .Join(
                _context.Roles.AsNoTracking().Where(r => r.TenantId == tenantId),
                assignment => assignment.RoleId,
                role => role.Id,
                (_, role) => role.Name)
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

    public async Task<bool> AssignToUserAsync(
        Guid tenantId,
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        // Local covers assignments added earlier in this unit of work but not yet saved.
        var exists = _context.TenantUserRoles.Local
                         .Any(a => a.TenantUserId == userId && a.RoleId == roleId) ||
                     await _context.TenantUserRoles
                         .AnyAsync(a => a.TenantUserId == userId && a.RoleId == roleId, cancellationToken);
        if (exists) return false;

        _context.TenantUserRoles.Add(new TenantUserRole(tenantId, userId, roleId));
        return true;
    }

    public async Task<bool> RevokeFromUserAsync(
        Guid tenantId,
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _context.TenantUserRoles
            .FirstOrDefaultAsync(
                a => a.TenantId == tenantId && a.TenantUserId == userId && a.RoleId == roleId,
                cancellationToken);
        if (assignment == null) return false;

        _context.TenantUserRoles.Remove(assignment);
        return true;
    }
}
