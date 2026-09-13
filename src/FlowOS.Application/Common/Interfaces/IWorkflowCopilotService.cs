using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Domain.Blueprints;

namespace FlowOS.Application.Common.Interfaces;

public interface IWorkflowCopilotService
{
    Task<GenerateBlueprintCopilotResponse> GenerateBlueprintAsync(
        string prompt, 
        WorkflowClassBlueprint? currentBlueprint = null, 
        string mode = "create", 
        CancellationToken cancellationToken = default);
}
