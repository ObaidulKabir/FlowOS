using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowOS.Domain.Entities.Orchestration;

/// <summary>
/// An immutable, versioned snapshot of an agent's definition (prompt, tools, model provider, policy).
/// AgentVersion is the "Law" side of the agent — once published it is never mutated, so historical
/// executions can always be replayed against the exact configuration that was active.
///
/// Relationship:
///   AgentDefinition (mutable profile) --publishes--> AgentVersion (immutable snapshot)
///                                                           |
///                                                           v
///                                                   AgentTaskJob / AgentExecutionRecord
/// </summary>
public sealed class AgentVersion
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Stable agent identity that groups all versions together.</summary>
    public string AgentId { get; private set; } = string.Empty;

    /// <summary>Monotonically increasing version counter within an AgentId.</summary>
    public int Version { get; private set; }

    /// <summary>Human-readable label, e.g. "v1", "v2-beta".</summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>Canonical role description for the agent, e.g. "Market Research Agent".</summary>
    public string Role { get; private set; } = string.Empty;

    /// <summary>
    /// Provider alias used at the time this version was published (e.g. "gpt-4o", "claude-3-5-sonnet").
    /// Stored verbatim — changes to the provider registry do not affect historical versions.
    /// </summary>
    public string ProviderAlias { get; private set; } = string.Empty;

    /// <summary>Prompt alias resolved at publish time.</summary>
    public string? PromptAlias { get; private set; }

    /// <summary>Frozen ordered list of tool aliases available to this version.</summary>
    public IReadOnlyList<string> ToolAliases { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Autonomy level: L0=Advisory, L1=Supervised, L2=GovernnedAutonomous, L3=AdaptiveAutonomous.
    /// Subject to the most restrictive applicable tenant/objective/team policy at runtime.
    /// </summary>
    public AgentAutonomyLevel AutonomyLevel { get; private set; }

    /// <summary>
    /// AutoCommit threshold (0.0–1.0). Null means never auto-commit regardless of confidence.
    /// Corresponds to the existing AutoCommitPolicy.AllowAutoCommit mechanism.
    /// </summary>
    public double? AutoCommitThreshold { get; private set; }

    /// <summary>
    /// Subset of workflow events this agent version is allowed to propose.
    /// Null means unrestricted (subject to state-machine Law at execution time).
    /// </summary>
    public IReadOnlyList<string>? AllowedEvents { get; private set; }

    /// <summary>Free-form capability tags, e.g. ["financial-analysis", "web-search"].</summary>
    public IReadOnlyList<string> CapabilityTags { get; private set; } = Array.Empty<string>();

    public bool IsDeprecated { get; private set; }

    public DateTime PublishedAtUtc { get; private set; }
    public DateTime? DeprecatedAtUtc { get; private set; }

    /// <summary>
    /// The UserId or system identifier that published this version.
    /// Never contains personal data — use a hashed/opaque identifier.
    /// </summary>
    public string PublishedBy { get; private set; } = string.Empty;

    // EF Core constructor
    private AgentVersion() { }

    /// <summary>
    /// Creates a new immutable version snapshot. Called when an agent profile is published.
    /// </summary>
    public static AgentVersion Publish(
        Guid tenantId,
        string agentId,
        int version,
        string label,
        string role,
        string providerAlias,
        string? promptAlias,
        IReadOnlyList<string> toolAliases,
        AgentAutonomyLevel autonomyLevel,
        double? autoCommitThreshold,
        IReadOnlyList<string>? allowedEvents,
        IReadOnlyList<string>? capabilityTags,
        string publishedBy,
        DateTime publishedAtUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("AgentId is required.", nameof(agentId));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), "Version must be >= 1.");
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role is required.", nameof(role));
        if (string.IsNullOrWhiteSpace(providerAlias))
            throw new ArgumentException("ProviderAlias is required.", nameof(providerAlias));
        if (autoCommitThreshold.HasValue && (autoCommitThreshold < 0.0 || autoCommitThreshold > 1.0))
            throw new ArgumentOutOfRangeException(nameof(autoCommitThreshold), "Must be between 0.0 and 1.0.");
        if (string.IsNullOrWhiteSpace(publishedBy))
            throw new ArgumentException("PublishedBy is required.", nameof(publishedBy));

        return new AgentVersion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentId = agentId.Trim(),
            Version = version,
            Label = string.IsNullOrWhiteSpace(label) ? $"v{version}" : label.Trim(),
            Role = role.Trim(),
            ProviderAlias = providerAlias.Trim(),
            PromptAlias = string.IsNullOrWhiteSpace(promptAlias) ? null : promptAlias.Trim(),
            ToolAliases = toolAliases?.Select(t => t.Trim()).Where(t => t.Length > 0).ToList()
                          ?? Array.Empty<string>(),
            AutonomyLevel = autonomyLevel,
            AutoCommitThreshold = autoCommitThreshold,
            AllowedEvents = allowedEvents?.Select(e => e.Trim()).Where(e => e.Length > 0).ToList(),
            CapabilityTags = capabilityTags?.Select(c => c.Trim()).Where(c => c.Length > 0).ToList()
                             ?? Array.Empty<string>(),
            PublishedBy = publishedBy.Trim(),
            PublishedAtUtc = AsUtc(publishedAtUtc),
            IsDeprecated = false
        };
    }

    /// <summary>
    /// Marks this version as deprecated. Deprecation is a soft signal — existing executions
    /// that hold a reference to this version continue to completion. New orchestrations should
    /// prefer a higher version.
    /// </summary>
    public void Deprecate(DateTime deprecatedAtUtc)
    {
        if (IsDeprecated)
            return;

        IsDeprecated = true;
        DeprecatedAtUtc = AsUtc(deprecatedAtUtc);
    }

    /// <summary>
    /// Returns true if this version satisfies all requested capability tags.
    /// </summary>
    public bool SatisfiesCapabilities(IReadOnlyCollection<string> requiredTags)
    {
        if (requiredTags == null || requiredTags.Count == 0)
            return true;

        return requiredTags.All(req =>
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
/// Maps to ADR-010: autonomy is policy-controlled, not prompt-controlled.
/// L0–L3 correspond to the four levels described in the architecture document.
/// At runtime the most restrictive applicable policy (tenant → objective → team → task) wins.
/// </summary>
public enum AgentAutonomyLevel
{
    /// <summary>L0 — Agent suggests; a human or supervisor decides before any action is taken.</summary>
    Advisory = 0,

    /// <summary>L1 — Agent executes after explicit approval of each action.</summary>
    Supervised = 1,

    /// <summary>L2 — Agent executes within predefined, policy-enforced boundaries without per-action approval.</summary>
    GovernnedAutonomous = 2,

    /// <summary>L3 — Agent can replan and delegate within approved boundaries; subject to ADR-003 governance.</summary>
    AdaptiveAutonomous = 3
}
