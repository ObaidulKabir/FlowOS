using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using FlowOS.Application.Commands.Security;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Security.Interfaces;
using FlowOS.Security.Models;

namespace FlowOS.Application.Handlers.Security;

public class RoleCommandHandlers : 
    IRequestHandler<CreateRoleCommand, Guid>,
    IRequestHandler<AddCapabilityToRoleCommand, bool>,
    IRequestHandler<RemoveCapabilityFromRoleCommand, bool>,
    IRequestHandler<AssignRoleToUserCommand, bool>,
    IRequestHandler<RevokeRoleFromUserCommand, bool>,
    IRequestHandler<GetRoleByIdQuery, Role?>,
    IRequestHandler<ListRolesQuery, IReadOnlyList<Role>>,
    IRequestHandler<ListUserRolesQuery, IReadOnlyList<string>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICapabilityService _capabilityService;

    public RoleCommandHandlers(IUnitOfWork unitOfWork, ICapabilityService capabilityService)
    {
        _unitOfWork = unitOfWork;
        _capabilityService = capabilityService;
    }

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var exists = await _unitOfWork.Roles
            .ExistsByNameAsync(request.TenantId, request.RoleName, cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException($"Role '{request.RoleName}' already exists.");
        }

        var role = new Role(request.TenantId, request.RoleName);

        _unitOfWork.Roles.Add(role);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _capabilityService.Invalidate(request.TenantId, new[] { request.RoleName });

        return role.Id;
    }

    public async Task<bool> Handle(AddCapabilityToRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Roles
            .GetByIdAsync(request.RoleId, request.TenantId, cancellationToken);

        if (role == null) return false;

        role.AddPermission(request.CapabilityCode);
        _unitOfWork.Roles.MarkPermissionsModified(role);
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _capabilityService.Invalidate(request.TenantId, new[] { role.Name });
        return true;
    }

    public async Task<bool> Handle(RemoveCapabilityFromRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Roles
            .GetByIdAsync(request.RoleId, request.TenantId, cancellationToken);

        if (role == null) return false;

        if (role.RemovePermission(request.CapabilityCode))
        {
            _unitOfWork.Roles.MarkPermissionsModified(role);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _capabilityService.Invalidate(request.TenantId, new[] { role.Name });
        }

        return true;
    }

    public async Task<bool> Handle(AssignRoleToUserCommand request, CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Roles
            .GetByIdAsync(request.RoleId, request.TenantId, cancellationToken);
        if (role == null) return false;

        var assigned = await _unitOfWork.Roles
            .AssignToUserAsync(request.TenantId, request.UserId, request.RoleId, cancellationToken);
        if (assigned)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<bool> Handle(RevokeRoleFromUserCommand request, CancellationToken cancellationToken)
    {
        var revoked = await _unitOfWork.Roles
            .RevokeFromUserAsync(request.TenantId, request.UserId, request.RoleId, cancellationToken);
        if (revoked)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return revoked;
    }

    public Task<Role?> Handle(GetRoleByIdQuery request, CancellationToken cancellationToken)
        => _unitOfWork.Roles.GetByIdAsync(request.Id, request.TenantId, cancellationToken);

    public Task<IReadOnlyList<Role>> Handle(ListRolesQuery request, CancellationToken cancellationToken)
        => _unitOfWork.Roles.ListAsync(request.TenantId, cancellationToken);

    public Task<IReadOnlyList<string>> Handle(ListUserRolesQuery request, CancellationToken cancellationToken)
        => _unitOfWork.Roles.ListAssignedRoleNamesAsync(request.TenantId, request.UserId, cancellationToken);
}
