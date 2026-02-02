namespace FlowOS.Domain.Enums;

public enum WorkflowClassScope
{
    Private = 0, // Default: Visible only to owning tenant
    Shared = 1,  // Submitted for review
    Public = 2   // Visible in public catalog
}
