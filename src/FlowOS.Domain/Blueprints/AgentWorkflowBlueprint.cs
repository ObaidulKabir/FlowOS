using System;
using System.Collections.Generic;
using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Blueprints;

// ─────────────────────────────────────────────────────────────────────────────
// AgentWorkflowBlueprint — declarative multi-agent coordination plan
//
// Mirrors WorkflowClassBlueprint in structure so the same visual authoring
// canvas, compiler, and validator can be extended to cover agentic workflows.
//
// ADR-006: an AgentWorkflowClass stores one of these blueprints as its
// Definition.  AgentWorkflowClass → AgentWorkflowExecution mirrors the
// WorkflowClass → WorkflowInstance pattern exactly.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Root declarative plan record.  Serialised as JSON in AgentWorkflowClass.Definition.
/// </summary>
public record AgentWorkflowBlueprint
{
    /// <summary>What the plan achieves — a plain-language objective statement.</summary>
    public string Objective { get; init; } = string.Empty;

    /// <summary>
    /// Ordered/graph-structured agent steps.  Each step maps to one AgentTeam
    /// execution.  Routing (sequential, parallel, conditional) is expressed via
    /// <see cref="AgentStepBlueprint.NextSteps"/> just like <see cref="StepBlueprint"/>.
    /// </summary>
    public List<AgentStepBlueprint> Steps { get; init; } = new();

    /// <summary>Step id that the engine enters first.</summary>
    public string StartStepId { get; init; } = string.Empty;

    /// <summary>Cross-step policies: retry budgets, rollback strategy, global SLA.</summary>
    public AgentCoordinationRulesBlueprint CoordinationRules { get; init; } = new();

    /// <summary>
    /// ADR-007: capabilities this plan declares. A step or role may only require a
    /// capability named here, so a typo cannot widen what the plan permits.
    /// Reuses <see cref="CapabilityBlueprint"/> from the human workflow pack.
    /// </summary>
    public List<CapabilityBlueprint> Capabilities { get; init; } = new();

    /// <summary>
    /// ADR-007: human business roles a <see cref="AgentStepType.HumanTask"/> step may be
    /// assigned to. Reuses <see cref="RoleBlueprint"/>, including its resolution
    /// strategies, rather than inventing a second role model.
    /// </summary>
    public List<RoleBlueprint> Roles { get; init; } = new();

    /// <summary>
    /// JSON Schema for the shared working memory passed between steps.
    /// Validated by the engine on each step handoff.
    /// </summary>
    public string? ContextSchema { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Step
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One node in the agent coordination graph.  Each step is executed by a
/// single AgentTeam.  Routing, SLA, and autonomy constraints are declared here
/// — the engine never infers them from runtime state.
/// </summary>
public record AgentStepBlueprint
{
    /// <summary>Unique identifier within the plan (e.g. "DataGathering", "RiskAssessment").</summary>
    public string StepId { get; init; } = string.Empty;

    /// <summary>
    /// Node kind: see <see cref="AgentStepType"/> — "Agent" (default), "Fork", "Join" or "End".
    /// Declared rather than inferred, so the graph shape is unambiguous for the dashboard
    /// visualiser and the Phase 4B dispatcher.
    /// </summary>
    public string StepType { get; init; } = AgentStepType.Agent;

    /// <summary>Human-readable purpose of this step.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// ADR-007: who may complete this step — see <see cref="StepActor"/>: "Human",
    /// "Agent" or "Either". Defaults to "Agent", which is the common case in an agent
    /// plan. A <see cref="Consequential"/> step must be completable by a person, so its
    /// actor must be "Human" or "Either", or it must be a HumanTask.
    /// </summary>
    public string Actor { get; init; } = StepActor.Agent;

    /// <summary>
    /// ADR-007: for a HumanTask, the declared <see cref="AgentWorkflowBlueprint.Roles"/>
    /// entry whose holders may complete it. Not used on agent steps, which bind a team
    /// instead.
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// ADR-007: capabilities required to complete this step. Each must be declared in
    /// <see cref="AgentWorkflowBlueprint.Capabilities"/>. This is the execution gate;
    /// <see cref="Role"/> determines whose inbox the task appears in.
    /// </summary>
    public List<string> RequiredCapabilities { get; init; } = new();

    // ── Team binding ─────────────────────────────────────────────────────────

    /// <summary>
    /// The AgentTeam template id that handles this step.
    /// At dispatch time the engine creates a TeamExecution from this template.
    /// </summary>
    public Guid? AgentTeamId { get; init; }

    /// <summary>
    /// Logical role name used when no static AgentTeamId is set.
    /// The orchestrator resolves the team at runtime (e.g. "RiskAnalyst").
    /// </summary>
    public string? TeamRole { get; init; }

    // ── Routing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Event key → next step id.  Mirrors <see cref="StepBlueprint.NextSteps"/>.
    /// The External Orchestrator publishes one of these events to advance the plan.
    /// </summary>
    public Dictionary<string, string> NextSteps { get; init; } = new();

    /// <summary>
    /// Fork targets: step ids to execute concurrently. Only meaningful when
    /// <see cref="StepType"/> is "Fork". Convergence is declared on the join step's
    /// <see cref="InboundSteps"/>, never here.
    /// </summary>
    public List<string> Branches { get; init; } = new();

    /// <summary>
    /// Join policy when <see cref="StepType"/> is "Join": "WaitAll" or "WaitAny".
    /// </summary>
    public string JoinPolicy { get; init; } = "WaitAll";

    /// <summary>
    /// Inbound steps that must converge here. Only meaningful when
    /// <see cref="StepType"/> is "Join".
    /// </summary>
    public List<string> InboundSteps { get; init; } = new();

    // ── Autonomy ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Maximum autonomy level permitted for this step.
    /// Overrides the AgentTeam's own MaxAutonomyLevel when more restrictive.
    /// </summary>
    public string? MaxAutonomyLevel { get; init; }

    /// <summary>
    /// When true the team's proposed outcome is never auto-committed regardless
    /// of confidence — a human gate is required before the plan advances.
    /// Mirrors <see cref="StepBlueprint.Consequential"/>.
    /// </summary>
    public bool Consequential { get; init; }

    /// <summary>Auto-commit policy for this step.  Mirrors <see cref="StepAutoCommitBlueprint"/>.</summary>
    public AgentStepAutoCommitBlueprint? AutoCommit { get; init; }

    // ── SLA ──────────────────────────────────────────────────────────────────

    /// <summary>Per-step SLA declaration.  Mirrors <see cref="StepSlaBlueprint"/>.</summary>
    public AgentStepSlaBlueprint? Sla { get; init; }

    // ── Prompt & tools ───────────────────────────────────────────────────────

    /// <summary>
    /// Objective prompt injected into the team's context at dispatch time.
    /// Mirrors <see cref="StepBlueprint.AgentPrompt"/>.
    /// </summary>
    public string? AgentPrompt { get; init; }

    /// <summary>Tool names available to agents in this step.</summary>
    public List<string> AgentTools { get; init; } = new();

    // ── Retry / path limits ───────────────────────────────────────────────────

    /// <summary>
    /// Per-event travel caps (retry / resubmit paths).
    /// Mirrors <see cref="StepBlueprint.PathLimits"/>.
    /// </summary>
    public Dictionary<string, AgentPathTravelLimitBlueprint> PathLimits { get; init; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
// Auto-commit
// ─────────────────────────────────────────────────────────────────────────────

public record AgentStepAutoCommitBlueprint
{
    /// <summary>Minimum confidence score required to auto-commit the team's outcome.</summary>
    public double MinConfidence { get; init; } = 0.9;

    /// <summary>Committed events that are eligible for auto-commit.</summary>
    public List<string> AllowedEvents { get; init; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
// SLA
// ─────────────────────────────────────────────────────────────────────────────

public record AgentStepSlaBlueprint
{
    /// <summary>ISO 8601 duration, e.g. "PT4H".</summary>
    public string Duration { get; init; } = string.Empty;

    /// <summary>Event published when the SLA expires.</summary>
    public string TimeoutEvent { get; init; } = string.Empty;

    /// <summary>Step id to route to on SLA breach.  If null the plan fails closed.</summary>
    public string? EscalationStepId { get; init; }

    /// <summary>Human role notified on breach.</summary>
    public string? EscalationRole { get; init; }

    public bool IsInterrupting { get; init; } = true;
}

// ─────────────────────────────────────────────────────────────────────────────
// Path limits
// ─────────────────────────────────────────────────────────────────────────────

public record AgentPathTravelLimitBlueprint
{
    /// <summary>Maximum times this edge may be traveled (including first pass).</summary>
    public int MaxTravels { get; init; } = 3;

    /// <summary>Step id or END taken when the cap is exceeded.</summary>
    public string? OnExceeded { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Coordination rules
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Cross-step governance policies.  Applied by the engine across all steps in
/// the plan — individual step declarations take precedence where they conflict.
/// </summary>
public record AgentCoordinationRulesBlueprint
{
    /// <summary>Global retry budget across all steps before the plan is abandoned.</summary>
    public int MaxGlobalRetries { get; init; } = 3;

    /// <summary>
    /// Rollback strategy when a step fails and retries are exhausted.
    /// "Abandon" (default) — mark the plan failed and stop.
    /// "Compensate" — reserved for Phase 4B. The step record declares no compensation
    /// hooks today, so the engine has nothing to run in reverse; declaring it now records
    /// the author's intent only.
    /// </summary>
    public string RollbackStrategy { get; init; } = "Abandon";

    /// <summary>Global wall-clock deadline for the whole plan (ISO 8601 duration).</summary>
    public string? GlobalSla { get; init; }

    /// <summary>
    /// Maximum number of steps that may run in parallel at any point.
    /// 0 = unlimited.
    /// </summary>
    public int MaxParallelSteps { get; init; } = 0;
}
