namespace FlowOS.Application.Common.Exceptions;

public class WorkflowContextPayloadException : ArgumentException
{
    public IReadOnlyList<string> Errors { get; }

    public WorkflowContextPayloadException(IEnumerable<string> errors)
        : base("Workflow context payload validation failed: " + string.Join("; ", errors))
    {
        Errors = errors.ToList();
    }
}
