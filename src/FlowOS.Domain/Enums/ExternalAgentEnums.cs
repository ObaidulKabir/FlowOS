namespace FlowOS.Domain.Enums;

public enum ExternalAgentChangeStatus
{
    Pending = 0,
    Leased = 1,
    Processed = 2,
    Failed = 3,
    DeadLetter = 4
}

public enum ExternalAgentStepStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4
}
