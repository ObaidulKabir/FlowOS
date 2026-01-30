using System;
using FlowOS.Application.DTOs;
using MediatR;

namespace FlowOS.Application.Queries;

public class ValidateStateMachineTransitionQuery : IRequest<ValidateTransitionResult>
{
    public Guid TenantId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string CurrentState { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
}
