using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowOS.Domain.Entities.Orchestration;

/// <summary>
/// An orchestration collaboration container for a group of AI agents working toward a shared objective.
///
/// ADR-002 PERMANENT DESIGN CONSTRAINT:
///   AgentTeam (this class) and Team (FlowOS.Domain.Entities.Team) are SEPARATE domain concepts
///   that must never share a repository, migration table, or naming convention.
///
///   Team     = IAM role-hierarchy for HUMAN users (members, managers, capability gates, confirmHumanApproval).
///   AgentTeam = Orchestration container for AI AGENTS (multi-agent collaboration, dynamic membership).
///
/// An AgentTeam is a template/definition. At runtime, a TeamExecution (not yet implemented) is
/// instantiated from the template — mirroring the WorkflowClass → WorkflowInstance pattern.
/// </summary>
public sealed class AgentTeam
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>
    /// Human-readable name for the team template, e.g. "Market Intelligence Team".
    /// Does NOT correspond to Team.Name in the IAM domain.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>The orchestration objective this team template is designed to serve.</summary>
    public string? DefaultObjective { get; private set; }

    /// <summary>
    /// Model C hybrid composition: predefined member slots that the orchestrator can fill
    /// dynamically at runtime. Each slot declares a required capability tag and an optional
    /// default agent version. The orchestrator may substitute a different agent if the default
    /// is unavailable or a better match exists, within governance policies.
    /// </summary>
    public IReadOnlyList<AgentTeamSlot> Slots { get; private set; } = Array.Empty<AgentTeamSlot>();

    /// <summary>
    /// Capability tags the team as a whole can satisfy, derived from the union of slot capabilities.
    /// Persisted for fast capability-matching queries without joining to all slot records.
    /// </summary>
    public IReadOnlyList<string> CapabilityTags { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Maximum nesting depth for sub-teams (configurable limit per ADR-001).
    /// 0 means this team cannot have nested sub-teams.
    /// </summary>
    public int MaxNestingDepth { get; private set; } = 1;

    /// <summary>
    /// Most permissive autonomy level allowed for any member of this team.
    /// Individual slots may specify a more restrictive level; the runtime takes the minimum.
    /// </summary>
    public AgentAutonomyLevel MaxAutonomyLevel { get; private set; } = AgentAutonomyLevel.GovernnedAutonomous;

    public AgentTeamStatus Status { get; private set; } = AgentTeamStatus.Draft;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }

    // EF Core constructor
    private AgentTeam() { }

    public AgentTeam(
        Guid tenantId,
        string name,
        string description,
        string? defaultObjective,
        IReadOnlyList<AgentTeamSlot>? slots,
        AgentAutonomyLevel maxAutonomyLevel,
        int maxNestingDepth,
        DateTime createdAtUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (maxNestingDepth < 0 || maxNestingDepth > 5)
            throw new ArgumentOutOfRangeException(nameof(maxNestingDepth), "MaxNestingDepth must be between 0 and 5.");

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        DefaultObjective = string.IsNullOrWhiteSpace(defaultObjective) ? null : defaultObjective.Trim();
        MaxAutonomyLevel = maxAutonomyLevel;
        MaxNestingDepth = maxNestingDepth;
        Status = AgentTeamStatus.Draft;
        CreatedAtUtc = AsUtc(createdAtUtc);

        SetSlots(slots ?? Array.Empty<AgentTeamSlot>(), createdAtUtc);
    }

    /// <summary>
    /// Updates the team template's profile. Only allowed while in Draft or Active status.
    /// </summary>
    public void UpdateProfile(
        string name,
        string description,
        string? defaultObjective,
        AgentAutonomyLevel maxAutonomyLevel,
        int maxNestingDepth,
        DateTime updatedAtUtc)
    {
        if (Status == AgentTeamStatus.Archived)
            throw new InvalidOperationException("An archived AgentTeam cannot be modified.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (maxNestingDepth < 0 || maxNestingDepth > 5)
            throw new ArgumentOutOfRangeException(nameof(maxNestingDepth));

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        DefaultObjective = string.IsNullOrWhiteSpace(defaultObjective) ? null : defaultObjective.Trim();
        MaxAutonomyLevel = maxAutonomyLevel;
        MaxNestingDepth = maxNestingDepth;
        UpdatedAtUtc = AsUtc(updatedAtUtc);
    }

    /// <summary>
    /// Replaces the slot definitions. Existing runtime executions referencing this template are
    /// unaffected — they hold a snapshot of the slot configuration at instantiation time.
    /// </summary>
    public void SetSlots(IReadOnlyList<AgentTeamSlot> slots, DateTime updatedAtUtc)
    {
        if (Status == AgentTeamStatus.Archived)
            throw new InvalidOperationException("An archived AgentTeam cannot be modified.");

        var roleNames = slots.Select(s => s.RoleName).ToList();
        if (roleNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != roleNames.Count)
            throw new ArgumentException("Slot role names must be unique within a team.", nameof(slots));

        Slots = slots.ToList();
        CapabilityTags = slots
            .SelectMany(s => s.RequiredCapabilities)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();

        UpdatedAtUtc = AsUtc(updatedAtUtc);
    }

    /// <summary>
    /// Activates the team template, making it available for orchestration.
    /// A team must have at least one slot before it can be activated.
    /// </summary>
    public void Activate(DateTime activatedAtUtc)
    {
        if (Status == AgentTeamStatus.Active)
            return;
        if (Status == AgentTeamStatus.Archived)
            throw new InvalidOperationException("An archived AgentTeam cannot be re-activated.");
        if (Slots.Count == 0)
            throw new InvalidOperationException("An AgentTeam must have at least one slot before activation.");

        Status = AgentTeamStatus.Active;
        ActivatedAtUtc = AsUtc(activatedAtUtc);
        UpdatedAtUtc = ActivatedAtUtc;
    }

    /// <summary>
    /// Archives the team template. Existing runtime executions continue to completion.
    /// New orchestrations will not select this template.
    /// </summary>
    public void Archive(DateTime archivedAtUtc)
    {
        if (Status == AgentTeamStatus.Archived)
            return;

        Status = AgentTeamStatus.Archived;
        ArchivedAtUtc = AsUtc(archivedAtUtc);
        UpdatedAtUtc = ArchivedAtUtc;
    }

    /// <summary>
    /// Returns the slot for the given role name, or null if no such slot is defined.
    /// </summary>
    public AgentTeamSlot? GetSlot(string roleName) =>
        Slots.FirstOrDefault(s => string.Equals(s.RoleName, roleName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Computes how many of the required capability tags are satisfied by this team's slot definitions.
    /// </summary>
    public int GetCapabilityMatchScore(IReadOnlyCollection<string> requiredCapabilities)
    {
        if (requiredCapabilities == null || requiredCapabilities.Count == 0) return 0;
        return requiredCapabilities.Count(req =>
            CapabilityTags.Any(cap => string.Equals(cap, req, StringComparison.OrdinalIgnoreCase)));
    }

    private static DateTime AsUtc(DateTime dt) =>
        dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
}

/// <summary>
/// A predefined role slot within an AgentTeam template (Model C hybrid composition).
/// At runtime the orchestrator fills the slot with a matching AgentVersion, using the
/// DefaultAgentId as a starting point and substituting if unavailable or suboptimal.
/// </summary>
public sealed record AgentTeamSlot(
    /// <summary>Unique role name within the team, e.g. "Researcher", "Reviewer".</summary>
    string RoleName,

    /// <summary>
    /// Capabilities required of any agent filling this slot.
    /// The orchestrator uses these for capability-matching when selecting a substitute.
    /// </summary>
    IReadOnlyList<string> RequiredCapabilities,

    /// <summary>
    /// Default agent identity (AgentVersion.AgentId) for this slot.
    /// Null means the orchestrator must discover an agent at runtime.
    /// </summary>
    string? DefaultAgentId,

    /// <summary>
    /// Most permissive autonomy level for this specific slot.
    /// Overrides the team-level MaxAutonomyLevel downward (never upward).
    /// </summary>
    AgentAutonomyLevel? AutonomyLevelOverride,

    /// <summary>Whether this slot is required for the team to start, or optional.</summary>
    bool IsRequired);

public enum AgentTeamStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}
