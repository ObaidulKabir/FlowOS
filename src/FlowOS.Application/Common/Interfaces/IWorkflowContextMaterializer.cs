using FlowOS.Application.Services;
using FlowOS.Domain.Entities;

namespace FlowOS.Application.Common.Interfaces;

public interface IWorkflowContextMaterializer
{
    Task<WorkflowContextCompilationPackage> ActivateAsync(
        WorkflowContextBinding binding,
        WorkflowContextBindingRevision revision,
        CancellationToken cancellationToken = default);
}
