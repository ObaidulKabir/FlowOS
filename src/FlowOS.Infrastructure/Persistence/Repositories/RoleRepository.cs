using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
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

    public Task<Role?> GetByIdAsync(Guid roleId, Guid tenantId, CancellationToken cancellationToken = default)
        => _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId && r.TenantId == tenantId, cancellationToken);

    public void Add(Role role) => _context.Roles.Add(role);

    public void MarkPermissionsModified(Role role)
        => _context.Entry(role).Property(r => r.Permissions).IsModified = true;
}
