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
    public const string AllTenants = "all";
    public const int DefaultAllTenantsLimit = 500;
    public const int MaxAllTenantsLimit = 1000;

    /// <summary>True only for a platform administrator who asked for scope=all.</summary>
    public static bool WantsAllTenants(ICurrentUser currentUser, string? scope) =>
        string.Equals(scope?.Trim(), AllTenants, StringComparison.OrdinalIgnoreCase) &&
        TenantIdentityRules.IsPlatformAdministrator(currentUser.TenantId, currentUser.Roles);

    public static int ClampLimit(int? requested) =>
        Math.Clamp(requested ?? DefaultAllTenantsLimit, 1, MaxAllTenantsLimit);

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
