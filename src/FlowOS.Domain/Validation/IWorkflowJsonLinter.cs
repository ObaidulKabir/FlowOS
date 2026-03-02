namespace FlowOS.Domain.Validation;

public record LintError(string Code, string Message, int Line, int Column, string Path, string Category);

public interface IWorkflowJsonLinter
{
    IEnumerable<LintError> Lint(string jsonContent);
}
