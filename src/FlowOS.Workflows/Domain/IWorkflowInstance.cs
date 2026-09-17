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
        List<string> ActiveStepIds { get; }
        Dictionary<string, int> PathTravelCounts { get; }

        void AdvanceTo(string nextStepId);
        void ForkTo(IEnumerable<string> branchStepIds);
        void CompleteBranch(string completedStepId, string? nextStepInBranch = null);
        void JoinTo(string continuationStepId);
        void SetCurrentState(string state);
        void Complete();
        void Wait();
    }
}