using System.Collections.Generic;

namespace FlowOS.Domain.Blueprints;

// ─────────────────────────────────────────────────────────────────────────────
// AgentTeamBlueprint — declarative team roster (ADR-007 §8)
//
// "An optional blueprint is documentation; a declaration that imperative edits
// can bypass is not a boundary." AgentTeamBlueprint is meant to become the only
// way a team comes into being, with AgentTeam as compiler output — mirroring
// WorkflowClassBlueprint → WorkflowClassCompiler → WorkflowStepDefinition.
//
// Today: structural authoring and validation only (ValidateAgentTeamBlueprintCommand,
// the validate_agent_team_blueprint MCP tool). There is no AgentTeamClass entity yet
// and nothing stores a team blueprint — this is the vocabulary a team step will bind
// to, published incrementally so the shape is reviewable before persistence,
// compilation to AgentTeam, and the AgentTeamStatus lifecycle question (ADR-007's own
// "Open decision for review") are implemented in a follow-up.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A declarative team roster: the positions ("slots") a multi-agent plan can bind to,
/// each with a role name and the capabilities an agent must have to fill it.
///
/// This is deliberately not where an individual agent is defined — that is
/// <see cref="AgentDefinitionBlueprint"/>, application-scoped and declared inside an
/// <see cref="AgentWorkflowBlueprint"/>. A team blueprint only declares the roster shape;
/// which concrete agent fills a slot is decided where the team is used.
/// </summary>
public record AgentTeamBlueprint
{
    /// <summary>Human-readable team name, e.g. "RiskAssessmentTeam".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>What this team exists to do — a plain-language statement.</summary>
    public string Objective { get; init; } = string.Empty;

    /// <summary>
    /// Autonomy ceiling for every member of this team. Composes downward with the
    /// tenant setting, any <see cref="AgentDefinitionBlueprint.MaxAutonomyLevel"/>, and a
    /// step override — see ADR-007 §4. This value may only narrow what a later link
    /// permits, never widen it.
    /// </summary>
    public string? MaxAutonomyLevel { get; init; }

    /// <summary>The roster. At least one slot is required — a team with no positions is not a team.</summary>
    public List<AgentTeamSlotBlueprint> Slots { get; init; } = new();
}

/// <summary>One position on the roster.</summary>
public record AgentTeamSlotBlueprint
{
    /// <summary>Unique within this team (e.g. "RiskAnalyst"). What a plan step's team-role binding names.</summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary>
    /// Capability tags an agent must have to fill this slot — e.g. "ticket-system.search".
    /// Checked for shape only here; whether an actual agent satisfies them is a
    /// dispatch-time question, same timing as every other capability tag in this chain.
    /// </summary>
    public List<string> RequiredCapabilities { get; init; } = new();

    /// <summary>
    /// The path every new blueprint should use: an <see cref="AgentDefinitionBlueprint.Alias"/>
    /// declared in whichever <see cref="AgentWorkflowBlueprint"/> uses this team. Not resolved
    /// here — a team blueprint has no workflow pack of its own to resolve it against; it is
    /// checked where the team is actually bound to a plan.
    /// </summary>
    public string? DefaultDefinitionAlias { get; init; }

    /// <summary>
    /// Deprecated escape hatch: a tenant-scoped <c>AgentProfileConfiguration</c> alias, for a
    /// slot that must resolve directly to one because no portable
    /// <see cref="AgentDefinitionBlueprint"/> has been declared yet. Prefer
    /// <see cref="DefaultDefinitionAlias"/> — this reintroduces the tenant-scoping defect
    /// ADR-007 §9 corrects.
    /// </summary>
    public string? DefaultProfileAlias { get; init; }
}
