using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowOS.Domain.Entities;

/// <summary>
/// A team is a group of tenant users responsible for handling a specific type of work.
/// Teams have a configurable hierarchy of levels (e.g. Member → Manager → Admin) and
/// declare capabilities that are matched against workflow step requirements when the
/// system needs to suggest or assign a team.
/// </summary>
public class Team
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>Capabilities this team can handle (e.g. "artwork.review", "production.scheduling").</summary>
    public List<string> Capabilities { get; private set; } = new();

    /// <summary>Ordered hierarchy levels, lowest to highest authority.</summary>
    public List<TeamHierarchyLevel> Hierarchy { get; private set; } = new();

    /// <summary>Members of this team, each assigned to a hierarchy level.</summary>
    public List<TeamMember> Members { get; private set; } = new();

    // EF Core constructor
    protected Team()
    {
        Name = null!;
        Description = null!;
    }

    public Team(Guid tenantId, string name, string description, List<string>? capabilities = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        Capabilities = capabilities?.Select(c => c.Trim()).Where(c => c.Length > 0).ToList() ?? new List<string>();
        Hierarchy = new List<TeamHierarchyLevel>();
        Members = new List<TeamMember>();
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateProfile(string name, string description, List<string>? capabilities = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        if (capabilities != null)
            Capabilities = capabilities.Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the hierarchy levels for this team. Levels are ordered from lowest (0) to highest authority.
    /// Existing members whose level name is removed will be unaffected until explicitly reassigned.
    /// </summary>
    public void SetHierarchy(List<TeamHierarchyLevel> levels)
    {
        if (levels == null || levels.Count == 0)
            throw new ArgumentException("A team must have at least one hierarchy level.", nameof(levels));

        // Ensure unique names and continuous ordering
        var names = levels.Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count != levels.Count)
            throw new ArgumentException("Hierarchy level names must be unique.", nameof(levels));

        Hierarchy = levels.OrderBy(l => l.Order).ToList();
        UpdatedAt = DateTime.UtcNow;
    }

    public TeamMember AddMember(Guid tenantUserId, string hierarchyLevelName)
    {
        if (tenantUserId == Guid.Empty) throw new ArgumentException("TenantUserId is required.", nameof(tenantUserId));
        if (string.IsNullOrWhiteSpace(hierarchyLevelName)) throw new ArgumentNullException(nameof(hierarchyLevelName));

        var level = Hierarchy.FirstOrDefault(h =>
            string.Equals(h.Name, hierarchyLevelName, StringComparison.OrdinalIgnoreCase));
        if (level == null)
            throw new InvalidOperationException($"Hierarchy level '{hierarchyLevelName}' does not exist in team '{Name}'.");

        if (Members.Any(m => m.TenantUserId == tenantUserId))
            throw new InvalidOperationException($"User '{tenantUserId}' is already a member of team '{Name}'.");

        var member = new TeamMember(Id, tenantUserId, level.Name, level.Order);
        Members.Add(member);
        UpdatedAt = DateTime.UtcNow;
        return member;
    }

    public void RemoveMember(Guid tenantUserId)
    {
        var member = Members.FirstOrDefault(m => m.TenantUserId == tenantUserId);
        if (member != null)
        {
            Members.Remove(member);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Returns members at the specified hierarchy order level (0 = lowest).
    /// </summary>
    public IReadOnlyList<TeamMember> GetMembersAtLevel(int order)
    {
        return Members.Where(m => m.HierarchyOrder == order).ToList();
    }

    /// <summary>
    /// Returns the next higher hierarchy level after the given order, or null if at the top.
    /// </summary>
    public TeamHierarchyLevel? GetNextHigherLevel(int currentOrder)
    {
        return Hierarchy
            .Where(h => h.Order > currentOrder)
            .OrderBy(h => h.Order)
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns the lowest hierarchy level (the first responders).
    /// </summary>
    public TeamHierarchyLevel? GetLowestLevel()
    {
        return Hierarchy.OrderBy(h => h.Order).FirstOrDefault();
    }

    /// <summary>
    /// Returns the highest hierarchy level (team admin).
    /// </summary>
    public TeamHierarchyLevel? GetHighestLevel()
    {
        return Hierarchy.OrderByDescending(h => h.Order).FirstOrDefault();
    }

    /// <summary>
    /// Computes a capability match score: how many of the required capabilities this team can handle.
    /// </summary>
    public int GetCapabilityMatchScore(IReadOnlyCollection<string> requiredCapabilities)
    {
        if (requiredCapabilities == null || requiredCapabilities.Count == 0) return 0;
        return requiredCapabilities.Count(req =>
            Capabilities.Any(cap => string.Equals(cap, req, StringComparison.OrdinalIgnoreCase)));
    }
}
