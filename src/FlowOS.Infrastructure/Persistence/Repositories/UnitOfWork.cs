using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
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
        Teams = new TeamRepository(context);
        StateMachines = new StateMachineDefinitionRepository(context);
        EventDefinitions = new EventDefinitionRepository(context);
        AgentInsights = new AgentInsightRepository(context);
        Policies = new PolicyRepository(context);
        WorkflowContextBindings = new WorkflowContextBindingRepository(context);
        WorkflowContextSnapshots = new WorkflowContextSnapshotRepository(context);
    }

    public IWorkflowInstanceRepository WorkflowInstances { get; }
    public IWorkflowDefinitionRepository WorkflowDefinitions { get; }
    public IWorkflowClassRepository WorkflowClasses { get; }
    public IDomainEventRepository Events { get; }
    public IRoleRepository Roles { get; }
    public ITeamRepository Teams { get; }
    public IStateMachineDefinitionRepository StateMachines { get; }
    public IEventDefinitionRepository EventDefinitions { get; }
    public IAgentInsightRepository AgentInsights { get; }
    public IPolicyRepository Policies { get; }
    public IWorkflowContextBindingRepository WorkflowContextBindings { get; }
    public IWorkflowContextSnapshotRepository WorkflowContextSnapshots { get; }

    /// <summary>
    /// Saves all changes. A lost optimistic-concurrency race (for example two events applied to the
    /// same workflow instance at once) becomes <see cref="ConcurrencyConflictException"/>, which the
    /// API returns as 409 so the caller can re-read and retry (CR-06). Nothing is overwritten.
    /// </summary>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var entities = string.Join(", ", ex.Entries.Select(e => e.Metadata.ClrType.Name).Distinct());
            // SaveChanges is atomic, so nothing was written. Drop the rejected changes so later saves in
            // this scope (for example marking the idempotency record as failed) do not repeat or
            // half-apply them.
            _context.ChangeTracker.Clear();
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                $"The record was changed by another request ({entities}). Read it again and retry.",
                ex);
        }
    }
}
