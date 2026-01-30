using System;
using MediatR;
using FlowOS.Application.Common.Attributes; // Add this
using MediatR;
using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Application.Commands;

[RequiresCapability("workflow.start")]
public record StartWorkflowCommand(
    Guid TenantId,
    Guid? WorkflowDefinitionId = null,
    string? WorkflowName = null,
    int? Version = null,
    string InitialStepId = "Start",
    Guid? CorrelationId = null
) : IRequest<Guid>, IPolicySecuredCommand;

[RequiresCapability("event.publish")]
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
