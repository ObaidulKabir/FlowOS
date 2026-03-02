namespace FlowOS.Workflows.Enums;

public enum WorkflowStatus
{
    Draft,
    Published,
    Archived
}

public enum WorkflowStepType
{
    Command,
    SystemTask, // Added for explicit system tasks
    HumanTask,
    Timer,
    Decision,
    End
}
