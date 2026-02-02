namespace FlowOS.Domain.Enums;

public enum WorkflowClassStatus
{
    Draft = 0,      // Editable, not executable
    Published = 1,  // Immutable, executable (Private)
    Shared = 2,     // Immutable, under review
    Public = 3,     // Immutable template
    Deprecated = 4, // No new instances
    Abandoned = 5   // Soft-deleted/Archived (No new instances)
}
