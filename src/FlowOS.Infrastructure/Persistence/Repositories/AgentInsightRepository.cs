using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Application.ReadModels;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

public class AgentInsightRepository : IAgentInsightRepository
{
    private readonly FlowOSDbContext _context;

    public AgentInsightRepository(FlowOSDbContext context)
    {
        _context = context;
    }

    public Task<List<AgentInsightReadModel>> ListByWorkflowInstanceIdsAsync(IEnumerable<Guid> workflowInstanceIds, CancellationToken cancellationToken = default)
        => _context.AgentInsights
            .AsNoTracking()
            .Where(i => workflowInstanceIds.Contains(i.WorkflowInstanceId))
            .ToListAsync(cancellationToken);

    public Task<List<AgentInsightReadModel>> ListByWorkflowInstanceIdAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default)
        => _context.AgentInsights
            .AsNoTracking()
            .Where(i => i.WorkflowInstanceId == workflowInstanceId)
            .ToListAsync(cancellationToken);

    public void Add(AgentInsightReadModel insight) => _context.AgentInsights.Add(insight);
}
