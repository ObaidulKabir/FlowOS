using FlowOS.Core.Security;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.Services.Security;

public sealed record TenantSecurityProvisioningResult(
    int TenantsScanned,
    int RolesCreated,
    int CapabilitiesAdded);

/// <summary>
/// Idempotently provisions the reserved tenant IAM roles used by human Admin
/// identities and scoped application keys.
/// </summary>
public sealed class TenantSecurityProvisioningService
{
    private readonly FlowOSDbContext _context;
    private readonly ILogger<TenantSecurityProvisioningService> _logger;

    public TenantSecurityProvisioningService(
        FlowOSDbContext context,
        ILogger<TenantSecurityProvisioningService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<TenantSecurityProvisioningResult> EnsureTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));

        var result = await EnsureTenantsAsync([tenantId], cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<TenantSecurityProvisioningResult> BackfillAsync(
        CancellationToken cancellationToken = default)
    {
        var tenantIds = await _context.Tenants
            .AsNoTracking()
            .Select(tenant => tenant.TenantId)
            .ToListAsync(cancellationToken);

        var result = await EnsureTenantsAsync(tenantIds, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Tenant IAM provisioning scanned {TenantsScanned} tenants, created {RolesCreated} roles, and added {CapabilitiesAdded} capabilities.",
            result.TenantsScanned,
            result.RolesCreated,
            result.CapabilitiesAdded);

        return result;
    }

    private async Task<TenantSecurityProvisioningResult> EnsureTenantsAsync(
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken cancellationToken)
    {
        if (tenantIds.Count == 0)
            return new TenantSecurityProvisioningResult(0, 0, 0);

        var roles = await _context.Roles
            .Where(role => tenantIds.Contains(role.TenantId))
            .ToListAsync(cancellationToken);

        var rolesCreated = 0;
        var capabilitiesAdded = 0;

        foreach (var tenantId in tenantIds)
        {
            foreach (var reserved in TenantSecurityDefaults.ReservedRoles)
            {
                var role = roles.FirstOrDefault(candidate =>
                    candidate.TenantId == tenantId &&
                    string.Equals(candidate.Name, reserved.Key, StringComparison.OrdinalIgnoreCase));

                if (role == null)
                {
                    role = new Role(tenantId, reserved.Key);
                    _context.Roles.Add(role);
                    roles.Add(role);
                    rolesCreated++;
                }

                var changed = false;
                foreach (var capability in reserved.Value)
                {
                    if (role.Permissions.Contains(capability, StringComparer.OrdinalIgnoreCase))
                        continue;

                    role.AddPermission(capability);
                    capabilitiesAdded++;
                    changed = true;
                }

                if (changed && _context.Entry(role).State != EntityState.Added)
                    _context.Entry(role).Property(item => item.Permissions).IsModified = true;
            }
        }

        return new TenantSecurityProvisioningResult(
            tenantIds.Count,
            rolesCreated,
            capabilitiesAdded);
    }
}
