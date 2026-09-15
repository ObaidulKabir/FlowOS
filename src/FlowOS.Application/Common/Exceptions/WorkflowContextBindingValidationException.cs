using FlowOS.Domain.Validation;

namespace FlowOS.Application.Common.Exceptions;

public class WorkflowContextBindingValidationException : Exception
{
    public ValidationResult ValidationResult { get; }

    public WorkflowContextBindingValidationException(ValidationResult validationResult)
        : base("Workflow context binding validation failed.")
    {
        ValidationResult = validationResult;
    }
}
