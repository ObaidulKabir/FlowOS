using System;
using MediatR;
using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Application.Commands;

public record StartWorkflowCommand(
    Guid TenantId,
    Guid WorkflowDefinitionId,
    int Version,
    string InitialStepId,
    Guid? CorrelationId = null
) : IRequest<Guid>, IPolicySecuredCommand;

public record PublishEventCommand(
    Guid TenantId,
    Guid WorkflowInstanceId,
    string EventType, // Can be legacy string or Event ID
    Guid? CorrelationId = null,
    object? Payload = null // Added Payload support
) : IRequest<bool>, IPolicySecuredCommand;

public record CompleteTaskCommand(
    Guid TenantId,
    Guid WorkflowInstanceId,
    Guid TaskId, // Placeholder for future Task Aggregate
    Guid? CorrelationId = null
) : IRequest<bool>, IPolicySecuredCommand;
