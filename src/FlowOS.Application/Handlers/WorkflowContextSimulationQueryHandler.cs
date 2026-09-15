using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using MediatR;

namespace FlowOS.Application.Handlers;

public sealed class WorkflowContextSimulationQueryHandler
    : IRequestHandler<SimulateWorkflowContextBindingQuery, WorkflowContextSimulationResultDto>
{
    private readonly IWorkflowContextSimulationService _simulationService;

    public WorkflowContextSimulationQueryHandler(
        IWorkflowContextSimulationService simulationService)
    {
        _simulationService = simulationService;
    }

    public Task<WorkflowContextSimulationResultDto> Handle(
        SimulateWorkflowContextBindingQuery request,
        CancellationToken cancellationToken)
        => _simulationService.SimulateAsync(
            request.TenantId,
            request.Request,
            cancellationToken);
}
