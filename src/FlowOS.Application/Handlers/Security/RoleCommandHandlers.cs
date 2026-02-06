using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FlowOS.Application.Commands.Security;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;

namespace FlowOS.Application.Handlers.Security;

public class RoleCommandHandlers : 
    IRequestHandler<CreateRoleCommand, Guid>,
    IRequestHandler<AddCapabilityToRoleCommand, bool>,
    IRequestHandler<AssignRoleToUserCommand, bool>
{
    private readonly FlowOSDbContext _context;

    public RoleCommandHandlers(FlowOSDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        // 1. Check if role exists
        var exists = await _context.Roles
            .AnyAsync(r => r.TenantId == request.TenantId && r.Name == request.RoleName, cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException($"Role '{request.RoleName}' already exists.");
        }

        // 2. Create Role
        var role = new Role(request.TenantId, request.RoleName);

        // 3. Persist
        _context.Roles.Add(role);
        await _context.SaveChangesAsync(cancellationToken);

        return role.Id;
    }

    public async Task<bool> Handle(AddCapabilityToRoleCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch Role
        var role = await _context.Roles
            .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.TenantId == request.TenantId, cancellationToken);

        if (role == null) return false;

        // 2. Add Capability (Permission)
        role.AddPermission(request.CapabilityCode);

        // Force update to ensure change tracking picks up the complex property change
        _context.Entry(role).Property(r => r.Permissions).IsModified = true;
        
        // 3. Save
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> Handle(AssignRoleToUserCommand request, CancellationToken cancellationToken)
    {
        // In a real system, we would have a UserRole mapping table.
        // For FlowOS Phase 1, we assume Identity Provider handles user-role mapping,
        // OR we store it in a local User table.
        // Given ICurrentUser gets roles from Claims, this command might need to update 
        // an external IDP or a local 'UserRoles' table if we are managing auth locally.
        
        // TODO: Implement User-Role assignment persistence.
        // For now, we will just return true to simulate success as we focus on Role definition.
        
        return await Task.FromResult(true);
    }
}
