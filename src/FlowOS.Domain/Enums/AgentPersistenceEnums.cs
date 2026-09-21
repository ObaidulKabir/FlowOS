namespace FlowOS.Domain.Enums;

public enum AgentTaskJobStatus
{
    Pending = 0,
    Claimed = 1,
    RetryScheduled = 2,
    Completed = 3,
    DeadLettered = 4,
    Cancelled = 5
}

public enum AgentTaskSource
{
    WorkflowEntry = 0,
    ManualRequest = 1,
    ApiRequest = 2,
    McpRequest = 3,
    Reconciliation = 4
}

public enum AgentExecutionMode
{
    Live = 0,
    Suggest = 1
}

public enum AgentExecutionStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3
}

public enum AgentExecutionOutcome
{
    Pending = 0,
    Suggested = 1,
    Committed = 2,
    Parked = 3,
    Skipped = 4,
    Failed = 5
}
