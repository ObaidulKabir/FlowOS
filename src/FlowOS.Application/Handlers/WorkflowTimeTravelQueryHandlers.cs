using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Queries;
using FlowOS.Core.Common.Interfaces;
using MediatR;

namespace FlowOS.Application.Handlers;

public class WorkflowTimeTravelQueryHandlers :
    IRequestHandler<GetWorkflowTimeTravelReplayQuery, WorkflowTimeTravelReplayDto?>,
    IRequestHandler<SimulateWorkflowForkQuery, WorkflowForkSimulationResultDto>
{
    private readonly IWorkflowTimeTravelService _timeTravelService;

    public WorkflowTimeTravelQueryHandlers(IWorkflowTimeTravelService timeTravelService)
    {
        _timeTravelService = timeTravelService ?? throw new ArgumentNullException(nameof(timeTravelService));
    }

    public async Task<WorkflowTimeTravelReplayDto?> Handle(GetWorkflowTimeTravelReplayQuery request, CancellationToken cancellationToken)
    {
        return await _timeTravelService.GetReplayTimelineAsync(request.TenantId, request.WorkflowInstanceId, cancellationToken);
    }

    public async Task<WorkflowForkSimulationResultDto> Handle(SimulateWorkflowForkQuery request, CancellationToken cancellationToken)
    {
        return await _timeTravelService.SimulateForkAsync(
            request.TenantId,
            request.WorkflowInstanceId,
            request.TargetStepIndex,
            request.AlternativeEvent,
            request.AlternativePayload,
            cancellationToken);
    }
}
