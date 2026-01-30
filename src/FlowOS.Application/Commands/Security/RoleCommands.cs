using System;
using System.Collections.Generic;
using MediatR;
using FlowOS.Application.Common.Interfaces;

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
    string UserId
) : IRequest<bool>, IPolicySecuredCommand;
