using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowOS.Core.Security;

/// <summary>
/// Production/Staging identity is JWT or API key. Header tenancy is Development mock only.
/// </summary>
public static class TenantIdentityRules
{
    public const string AllowMockAuthKey = "FlowOS:Identity:AllowMockAuth";
    public static readonly Guid PlatformTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DemoTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static bool AllowMockAuth(string? environmentName, string? configuredValue)
    {
        if (bool.TryParse(configuredValue, out var configured))
            return configured;

        return string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseTenant(string? value, out Guid tenantId)
    {
        if (Guid.TryParse(value, out tenantId) && tenantId != Guid.Empty)
            return true;

        tenantId = Guid.Empty;
        return false;
    }

    public static Guid? CredentialTenant(string? claimValue)
        => TryParseTenant(claimValue, out var id) ? id : null;

    public static bool IsPrivilegedRole(string? role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase);

    public static bool IsPlatformAdministrator(Guid tenantId, IEnumerable<string>? roles)
    {
        if (roles == null || !roles.Any(IsPrivilegedRole))
            return false;

        return tenantId == PlatformTenantId || tenantId == DemoTenantId;
    }

    public static bool IsDemoApiKey(string? suppliedApiKey) =>
        string.Equals(suppliedApiKey, "flowos_prod_secret_key_32_chars_min", StringComparison.Ordinal) ||
        string.Equals(suppliedApiKey, "local-development-key-change-me", StringComparison.Ordinal) ||
        string.Equals(suppliedApiKey, "YOUR_PRODUCTION_API_KEY", StringComparison.Ordinal);

    /// <summary>
    /// Production API keys cannot spoof X-Mock-Role. Full-tenant keys (<c>*</c>, demo playground)
    /// map to Admin so sandbox and service accounts keep workflow.start without header privilege.
    /// </summary>
    public static string ResolveApiKeyRole(IEnumerable<string>? scopes, bool isDemoKey)
    {
        if (isDemoKey)
            return "Admin";

        if (scopes == null)
            return "ApiKey";

        foreach (var scope in scopes)
        {
            if (string.IsNullOrWhiteSpace(scope))
                continue;
            if (scope == "*" || string.Equals(scope, "admin:*", StringComparison.OrdinalIgnoreCase))
                return "Admin";
        }

        return "ApiKey";
    }
}
