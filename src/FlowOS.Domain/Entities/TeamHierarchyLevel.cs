using System;

namespace FlowOS.Domain.Entities;

/// <summary>
/// Defines a hierarchy level within a <see cref="Team"/> (e.g. "Member", "Manager", "Admin").
/// </summary>
public class TeamHierarchyLevel
{
    public Guid Id { get; private set; }
    public Guid TeamId { get; private set; }
    public string Name { get; private set; }
    public int Order { get; private set; } // 0 = lowest (first responder), higher = more authority

    // EF Core constructor
    protected TeamHierarchyLevel()
    {
        Name = null!;
    }

    public TeamHierarchyLevel(Guid teamId, string name, int order)
    {
        if (teamId == Guid.Empty) throw new ArgumentException("TeamId is required.", nameof(teamId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

        Id = Guid.NewGuid();
        TeamId = teamId;
        Name = name.Trim();
        Order = order;
    }
}
