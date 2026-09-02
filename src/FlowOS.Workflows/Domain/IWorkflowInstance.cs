using FlowOS.Workflows.Enums;

namespace FlowOS.Workflows.Domain
{
    public interface IWorkflowInstance
    {
        DateTime? CompletedAt { get; }
        Guid? CorrelationId { get; }
        DateTime CreatedAt { get; }
        string CurrentStepId { get; }
        string? CurrentState { get; }
        Guid Id { get; }
        WorkflowInstanceStatus Status { get; }
        Guid TenantId { get; }
        Guid WorkflowClassId { get; }
        Guid WorkflowDefinitionId { get; }
        int WorkflowVersion { get; }

        void AdvanceTo(string nextStepId);
        void SetCurrentState(string state);
        void Complete();
        void Wait();
    }
}