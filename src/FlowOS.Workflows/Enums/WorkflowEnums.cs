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
    HumanTask,
    Timer,
    Decision
}
