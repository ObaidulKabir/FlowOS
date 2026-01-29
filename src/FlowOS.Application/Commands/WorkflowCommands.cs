using System;
using MediatR;

namespace FlowOS.Application.Commands;

public record StartWorkflowCommand(
    Guid TenantId,
    Guid WorkflowDefinitionId,
    int Version,
    string InitialStepId,
    Guid? CorrelationId = null
) : IRequest<Guid>;

public record PublishEventCommand(
    Guid TenantId,
    Guid WorkflowInstanceId,
    string EventType,
    Guid? CorrelationId = null
) : IRequest<bool>;

public record CompleteTaskCommand(
    Guid TenantId,
    Guid WorkflowInstanceId,
    Guid TaskId, // Placeholder for future Task Aggregate
    Guid? CorrelationId = null
) : IRequest<bool>;
