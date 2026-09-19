using System;

namespace FlowOS.Domain.Entities;

/// <summary>
/// Assignment of a tenant role to a tenant user. A user may hold several roles;
/// <see cref="TenantUser.Role"/> remains the primary role used for legacy single-role checks.
/// </summary>
public class TenantUserRole
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TenantUserId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTime AssignedAtUtc { get; private set; }

    protected TenantUserRole()
    {
    }

    public TenantUserRole(Guid tenantId, Guid tenantUserId, Guid roleId)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (tenantUserId == Guid.Empty) throw new ArgumentException("TenantUserId is required.", nameof(tenantUserId));
        if (roleId == Guid.Empty) throw new ArgumentException("RoleId is required.", nameof(roleId));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        TenantUserId = tenantUserId;
        RoleId = roleId;
        AssignedAtUtc = DateTime.UtcNow;
    }
}
