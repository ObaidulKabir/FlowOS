using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Application.Common.Interfaces.Persistence;

/// <summary>
/// Coordinates repositories that share a single persistence context and transaction boundary.
/// </summary>
public interface IUnitOfWork
{
    IWorkflowInstanceRepository WorkflowInstances { get; }
    IWorkflowDefinitionRepository WorkflowDefinitions { get; }
    IWorkflowClassRepository WorkflowClasses { get; }
    IDomainEventRepository Events { get; }
    IRoleRepository Roles { get; }
    ITeamRepository Teams { get; }
    IStateMachineDefinitionRepository StateMachines { get; }
    IEventDefinitionRepository EventDefinitions { get; }
    IAgentInsightRepository AgentInsights { get; }
    IPolicyRepository Policies { get; }
    IWorkflowContextBindingRepository WorkflowContextBindings { get; }
    IWorkflowContextSnapshotRepository WorkflowContextSnapshots { get; }

    /// <summary>
    /// ADR-005 Phase 3: TeamExecution runtime records.
    /// Used by AgentTaskCoordinator to update outcome fields and emit
    /// the AgentTaskCompleted change-feed event.
    /// </summary>
    ITeamExecutionRepository TeamExecutions { get; }

    /// <summary>
    /// ADR-006 Phase 4A: AgentWorkflowClass declarative plan templates.
    /// Dispatch machinery (AgentWorkflowExecution) arrives in Phase 4B.
    /// </summary>
    IAgentWorkflowClassRepository AgentWorkflowClasses { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
