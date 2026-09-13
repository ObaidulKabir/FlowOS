using System;
using System.Collections.Generic;
using FlowOS.Core.Common.Interfaces;
using MediatR;

namespace FlowOS.Application.Queries;

public record GetWorkflowTimeTravelReplayQuery(
    Guid TenantId,
    Guid WorkflowInstanceId
) : IRequest<WorkflowTimeTravelReplayDto?>;

public record SimulateWorkflowForkQuery(
    Guid TenantId,
    Guid WorkflowInstanceId,
    int TargetStepIndex,
    string AlternativeEvent,
    object? AlternativePayload = null
) : IRequest<WorkflowForkSimulationResultDto>;

public record GetWorkflowCompensationPathQuery(
    Guid TenantId,
    Guid WorkflowInstanceId,
    string? FailedStepId = null
) : IRequest<WorkflowCompensationPathDto?>;
