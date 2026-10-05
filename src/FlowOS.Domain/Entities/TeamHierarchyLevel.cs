using System;

namespace FlowOS.Domain.Entities;

/// <summary>
/// Defines a hierarchy level within a <see cref="Team"/> (e.g. "Member", "Manager", "Admin").
/// </summary>
public class TeamHierarchyLevel
{
    public string Name { get; private set; }
    public int Order { get; private set; } // 0 = lowest (first responder), higher = more authority

    // EF Core constructor
    protected TeamHierarchyLevel()
    {
        Name = null!;
    }

    public TeamHierarchyLevel(string name, int order)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

        Name = name.Trim();
        Order = order;
    }
}
