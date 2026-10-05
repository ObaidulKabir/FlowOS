using System;

namespace FlowOS.Domain.Entities;

/// <summary>
/// A member of a <see cref="Team"/>, linking a <see cref="TenantUser"/> to the team
/// at a specific hierarchy level. The <see cref="HierarchyOrder"/> determines escalation
/// order: lower values are notified first, higher values are notified during escalation.
/// </summary>
public class TeamMember
{
    public Guid Id { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid TenantUserId { get; private set; }

    /// <summary>The name of the hierarchy level this member belongs to (e.g. "Member", "Manager", "Admin").</summary>
    public string HierarchyLevelName { get; private set; }

    /// <summary>Numeric order of the hierarchy level (0 = lowest / first responder, higher = more authority).</summary>
    public int HierarchyOrder { get; private set; }

    public DateTime AssignedAtUtc { get; private set; }

    // EF Core constructor
    protected TeamMember()
    {
        HierarchyLevelName = null!;
    }

    public TeamMember(Guid teamId, Guid tenantUserId, string hierarchyLevelName, int hierarchyOrder)
    {
        if (teamId == Guid.Empty) throw new ArgumentException("TeamId is required.", nameof(teamId));
        if (tenantUserId == Guid.Empty) throw new ArgumentException("TenantUserId is required.", nameof(tenantUserId));
        if (string.IsNullOrWhiteSpace(hierarchyLevelName)) throw new ArgumentNullException(nameof(hierarchyLevelName));

        Id = Guid.NewGuid();
        TeamId = teamId;
        TenantUserId = tenantUserId;
        HierarchyLevelName = hierarchyLevelName.Trim();
        HierarchyOrder = hierarchyOrder;
        AssignedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Reassigns this member to a different hierarchy level within the same team.
    /// </summary>
    public void Reassign(string newLevelName, int newOrder)
    {
        if (string.IsNullOrWhiteSpace(newLevelName)) throw new ArgumentNullException(nameof(newLevelName));
        HierarchyLevelName = newLevelName.Trim();
        HierarchyOrder = newOrder;
    }
}
