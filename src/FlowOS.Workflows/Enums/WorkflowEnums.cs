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
    End,
    Fork,
    Join,
    SubWorkflow,
    /// <summary>
    /// ADR-003: Agent-actor workflow step. Mirrors HumanTask semantics — the workflow pauses
    /// and waits. An AI agent fills the actor slot. Agent decisions are proposals that flow
    /// through the existing AutoCommitEvaluator; the agent never directly transitions workflow state.
    /// </summary>
    AgentTask
}
