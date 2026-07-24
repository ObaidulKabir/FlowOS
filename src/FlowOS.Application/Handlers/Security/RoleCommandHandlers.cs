using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using FlowOS.Application.Commands.Security;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Security.Models;

namespace FlowOS.Application.Handlers.Security;

public class RoleCommandHandlers : 
    IRequestHandler<CreateRoleCommand, Guid>,
    IRequestHandler<AddCapabilityToRoleCommand, bool>,
    IRequestHandler<AssignRoleToUserCommand, bool>,
    IRequestHandler<GetRoleByIdQuery, Role?>
{
    private readonly IUnitOfWork _unitOfWork;

    public RoleCommandHandlers(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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
        return true;
    }

    public Task<bool> Handle(AssignRoleToUserCommand request, CancellationToken cancellationToken)
    {
        // User↔role assignment is not persisted yet (no user-role store in the domain model).
        throw new NotSupportedException(
            "Assigning roles to users is not implemented. Roles are currently resolved from auth claims (e.g. X-Mock-Role).");
    }

    public Task<Role?> Handle(GetRoleByIdQuery request, CancellationToken cancellationToken)
        => _unitOfWork.Roles.GetByIdAsync(request.Id, request.TenantId, cancellationToken);
}
