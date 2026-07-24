using System;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Security.Models;
using MediatR;

namespace FlowOS.Application.Commands.Security;

public record CreatePolicyCommand(Guid TenantId, string Name, string ConditionJson)
    : IRequest<Guid>, IPolicySecuredCommand;

public record GetPolicyByIdQuery(Guid TenantId, Guid Id)
    : IRequest<Policy?>;
