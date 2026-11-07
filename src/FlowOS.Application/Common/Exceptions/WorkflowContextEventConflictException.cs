namespace FlowOS.Application.Common.Exceptions;

/// <summary>
/// Thrown when activating a context binding would redefine an event already registered for the
/// tenant with an incompatible status, category, terminal flag, or payload schema (DX report F6: the
/// plain CTX-STATE-001 message named the conflicting event but not enough to act on it - which
/// definition it conflicts with, or which class/binding introduced it).
/// </summary>
public class WorkflowContextEventConflictException : InvalidOperationException
{
    public string ConflictingEventId { get; }
    public Guid ClassId { get; }
    public string ClassName { get; }
    public string ClassVersion { get; }
    public Guid BindingId { get; }
    public Guid BindingRevisionId { get; }

    public WorkflowContextEventConflictException(
        string message,
        string conflictingEventId,
        Guid classId,
        string className,
        string classVersion,
        Guid bindingId,
        Guid bindingRevisionId)
        : base(message)
    {
        ConflictingEventId = conflictingEventId;
        ClassId = classId;
        ClassName = className;
        ClassVersion = classVersion;
        BindingId = bindingId;
        BindingRevisionId = bindingRevisionId;
    }
}
