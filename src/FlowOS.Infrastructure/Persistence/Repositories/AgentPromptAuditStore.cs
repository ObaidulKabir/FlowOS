using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;

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
}
