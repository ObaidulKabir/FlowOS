using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Infrastructure.Persistence;

namespace FlowOS.Infrastructure.Persistence.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly FlowOSDbContext _context;

    public UnitOfWork(FlowOSDbContext context)
    {
        _context = context;
        WorkflowInstances = new WorkflowInstanceRepository(context);
        WorkflowDefinitions = new WorkflowDefinitionRepository(context);
        WorkflowClasses = new WorkflowClassRepository(context);
        Events = new DomainEventRepository(context);
        Roles = new RoleRepository(context);
        StateMachines = new StateMachineDefinitionRepository(context);
        EventDefinitions = new EventDefinitionRepository(context);
        AgentInsights = new AgentInsightRepository(context);
        Policies = new PolicyRepository(context);
    }

    public IWorkflowInstanceRepository WorkflowInstances { get; }
    public IWorkflowDefinitionRepository WorkflowDefinitions { get; }
    public IWorkflowClassRepository WorkflowClasses { get; }
    public IDomainEventRepository Events { get; }
    public IRoleRepository Roles { get; }
    public IStateMachineDefinitionRepository StateMachines { get; }
    public IEventDefinitionRepository EventDefinitions { get; }
    public IAgentInsightRepository AgentInsights { get; }
    public IPolicyRepository Policies { get; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
