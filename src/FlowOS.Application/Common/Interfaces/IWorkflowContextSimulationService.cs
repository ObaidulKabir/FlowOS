using FlowOS.Application.DTOs;

namespace FlowOS.Application.Common.Interfaces;

public interface IWorkflowContextSimulationService
{
    Task<WorkflowContextSimulationResultDto> SimulateAsync(
        Guid tenantId,
        WorkflowContextSimulationRequest request,
        CancellationToken cancellationToken = default);
}
