using System;
using FlowOS.Core.Interfaces;
using FlowOS.Core.Security;

namespace FlowOS.Api.Services;

/// <summary>
/// Lets a platform administrator read another tenant's data by passing ?tenantId=. Everyone else,
/// including tenant administrators and the demo tenant, always stays on their own tenant.
/// Use it for read endpoints only.
/// </summary>
public static class AdminTenantScope
{
    public static Guid ResolveRead(ICurrentUser currentUser, Guid? requestedTenantId)
    {
        var own = currentUser.TenantId;
        if (!requestedTenantId.HasValue || requestedTenantId.Value == Guid.Empty || requestedTenantId.Value == own)
            return own;

        return TenantIdentityRules.IsPlatformAdministrator(own, currentUser.Roles)
            ? requestedTenantId.Value
            : own;
    }
}
