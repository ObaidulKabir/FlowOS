using System;
using System.Collections.Generic;
using MediatR;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Security.Models;

namespace FlowOS.Application.Commands.Security;

public record CreateRoleCommand(
    Guid TenantId,
    string RoleName
) : IRequest<Guid>, IPolicySecuredCommand;

public record AddCapabilityToRoleCommand(
    Guid TenantId,
    Guid RoleId,
    string CapabilityCode
) : IRequest<bool>, IPolicySecuredCommand;

public record AssignRoleToUserCommand(
    Guid TenantId,
    Guid RoleId,
    Guid UserId
) : IRequest<bool>, IPolicySecuredCommand;

public record RevokeRoleFromUserCommand(
    Guid TenantId,
    Guid RoleId,
    Guid UserId
) : IRequest<bool>, IPolicySecuredCommand;

public record GetRoleByIdQuery(Guid TenantId, Guid Id) : IRequest<Role?>;

public record ListRolesQuery(Guid TenantId) : IRequest<IReadOnlyList<Role>>;

public record ListUserRolesQuery(Guid TenantId, Guid UserId) : IRequest<IReadOnlyList<string>>;
