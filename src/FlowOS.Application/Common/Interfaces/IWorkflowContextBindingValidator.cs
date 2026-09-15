using FlowOS.Domain.Entities;
using FlowOS.Domain.Validation;

namespace FlowOS.Application.Common.Interfaces;

public interface IWorkflowContextBindingValidator
{
    Task<ValidationResult> ValidateAsync(
        WorkflowContextBinding binding,
        WorkflowContextBindingRevision revision,
        CancellationToken cancellationToken = default);
}
