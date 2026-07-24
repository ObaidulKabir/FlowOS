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
    IStateMachineDefinitionRepository StateMachines { get; }
    IEventDefinitionRepository EventDefinitions { get; }
    IAgentInsightRepository AgentInsights { get; }
    IPolicyRepository Policies { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
