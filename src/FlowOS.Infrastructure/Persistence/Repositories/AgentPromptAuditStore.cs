using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Persistence.Repositories;

internal sealed class AgentPromptAuditStore : IAgentPromptAuditStore
{
    private readonly FlowOSDbContext _db;
    public AgentPromptAuditStore(FlowOSDbContext db) => _db = db;
    public Task RecordAsync(AgentPromptAuditRecord record, CancellationToken ct = default)
    {
        _db.AgentPromptAuditRecords.Add(record);
        return _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AgentPromptAuditRecord>> GetByWorkflowInstanceAsync(Guid tenantId, Guid? workflowInstanceId = null, CancellationToken ct = default)
    {
        var query = _db.AgentPromptAuditRecords
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        if (workflowInstanceId.HasValue)
            query = query.Where(x => x.WorkflowInstanceId == workflowInstanceId.Value);

        return await query
            .OrderByDescending(x => x.RecordedAtUtc)
            .Take(100) // safety limit
            .ToListAsync(ct);
    }
}
