using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Application.Billing;

/// <summary>
/// Renews expiring or lapsed tenant API keys when an administrator activates a plan, so approval
/// alone unblocks the tenant. Revoked keys and keys that never expire are not touched.
/// </summary>
public interface ITenantApiKeyRenewalService
{
    /// <summary>Default length of a renewal, in days from now.</summary>
    const int DefaultRenewalDays = 365;

    /// <returns>The number of keys whose expiry was extended.</returns>
    Task<int> ExtendExpiringKeysAsync(Guid tenantId, int days = DefaultRenewalDays, CancellationToken cancellationToken = default);
}
