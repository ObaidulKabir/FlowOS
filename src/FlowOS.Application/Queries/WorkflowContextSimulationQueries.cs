using FlowOS.Application.DTOs;
using MediatR;

namespace FlowOS.Application.Queries;

public sealed record SimulateWorkflowContextBindingQuery(
    Guid TenantId,
    WorkflowContextSimulationRequest Request)
    : IRequest<WorkflowContextSimulationResultDto>;
