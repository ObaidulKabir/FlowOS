using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Billing;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public sealed class TenantApiKeyRenewalService : ITenantApiKeyRenewalService
{
    private readonly FlowOSDbContext _db;
    private readonly TimeProvider _time;

    public TenantApiKeyRenewalService(FlowOSDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<int> ExtendExpiringKeysAsync(Guid tenantId, int days, CancellationToken cancellationToken = default)
    {
        if (days <= 0)
            return 0;

        var newExpiry = _time.GetUtcNow().UtcDateTime.AddDays(days);
        var keys = await _db.TenantApiKeys
            .Where(k => k.TenantId == tenantId && !k.IsRevoked && k.ExpiresAt != null)
            .ToListAsync(cancellationToken);

        var changed = keys.Count(k => k.ExtendExpiry(newExpiry));
        if (changed > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return changed;
    }
}
