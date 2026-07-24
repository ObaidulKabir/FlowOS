using System;
using MediatR;
using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Application.Commands.Admin;

public record PublishConfigurationResult(bool FoundConfigRoot, string Message, string? ConfigRoot);

public record PublishConfigurationCommand(Guid TenantId)
    : IRequest<PublishConfigurationResult>, IPolicySecuredCommand;
