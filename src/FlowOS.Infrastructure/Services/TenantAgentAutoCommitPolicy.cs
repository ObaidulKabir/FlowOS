using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public sealed class TenantAgentAutoCommitPolicy : ITenantAgentAutoCommitPolicy
{
    private readonly FlowOSDbContext _db;

    public TenantAgentAutoCommitPolicy(FlowOSDbContext db)
    {
        _db = db;
    }

    public async Task<bool> IsAutoCommitEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var enabled = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => (bool?)t.AgentAutoCommitEnabled)
            .FirstOrDefaultAsync(cancellationToken);
        // A tenant without a row (only in tests and sandboxes) keeps the previous behaviour.
        return enabled ?? true;
    }
}
