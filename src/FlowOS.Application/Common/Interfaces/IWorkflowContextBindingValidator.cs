using FlowOS.Domain.Entities;
using FlowOS.Domain.Validation;

namespace FlowOS.Application.Common.Interfaces;

public readonly record struct WorkflowContextBindingValidationOptions(
    bool RequirePublishedSource,
    bool RequireExistingTenantRoles)
{
    public static WorkflowContextBindingValidationOptions Strict { get; } = new(true, true);
    public static WorkflowContextBindingValidationOptions Simulation { get; } = new(false, false);
}

public interface IWorkflowContextBindingValidator
{
    Task<ValidationResult> ValidateAsync(
        WorkflowContextBinding binding,
        WorkflowContextBindingRevision revision,
        CancellationToken cancellationToken = default);

    Task<ValidationResult> ValidateAsync(
        WorkflowContextBinding binding,
        WorkflowContextBindingRevision revision,
        WorkflowContextBindingValidationOptions options,
        CancellationToken cancellationToken = default);
}
