using System.Collections.Generic;

namespace FlowOS.Domain.Validation;

public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationError> Errors { get; } = new();

    public void AddError(string code, string category, string message, string element)
    {
        Errors.Add(new ValidationError(code, category, message, element));
    }
}

public record ValidationError(string Code, string Category, string Message, string Element);
