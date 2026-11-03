# ADR-010 — Autonomy Is Policy-Controlled, Not Prompt-Controlled

**Status:** Accepted  
**Date:** 2026-11-03  
**Deciders:** Architecture  

## Context

Multi-agent systems risk privilege escalation if an agent can negotiate its own permission level through prompt engineering. The existing FlowOS `AutoCommitPolicy` already enforces bounded autonomy at the platform layer — the new orchestration system must extend, not duplicate or bypass, that mechanism.

## Decision

Autonomy levels are an extension of the existing `AutoCommitPolicy` / `RequiresHumanConfirmation` enforcement. They are configurable at four scopes; the **most restrictive applicable policy wins** at runtime:

| Scope | Example |
|---|---|
| Tenant | Platform-wide cap; overrides all |
| Objective | Set when the objective is created |
| Team | Set on the `AgentTeam` template (`MaxAutonomyLevel`) |
| Task / Slot | Set on the individual `AgentTeamSlot` (`AutonomyLevelOverride`) |

### Four autonomy levels

| Level | Name | Behaviour | Existing mechanism |
|---|---|---|---|
| L0 | Advisory | Agent suggests; human decides | `RequiresHumanConfirmation` = always |
| L1 | Supervised | Agent executes after per-action approval | `RequiresHumanConfirmation` = true per action |
| L2 | GovernnedAutonomous | Agent executes within predefined policy | `AutoCommitPolicy` with threshold + allowed events |
| L3 | AdaptiveAutonomous | Agent replans and delegates within approved boundaries | Extended `AutoCommitPolicy`; replan proposals validated by FlowOS |

### Enforcement rules

1. The system enforces the permission level **independently of the agent's prompt**. A prompt that claims L3 access while the applicable policy is L1 is rejected at the platform layer.
2. `AutoCommitThreshold` on `AgentVersion` maps directly to the existing `AutoCommitPolicy.Threshold` semantics.
3. `AllowedEvents` on `AgentVersion` constrains the events the agent may propose, independently of what the state machine permits (both constraints apply; the intersection is the effective boundary).
4. Agents that send external financial commitments or change critical business state are capped at L1 regardless of their declared level unless an explicit L2/L3 policy is in force.

### Implementation path

- Extend `AutoCommitPolicy` with an `AutonomyLevel` field (or a companion `AgentTaskAutonomyPolicy` value object) rather than creating a parallel policy system.
- The `AgentVersion.AutonomyLevel` field (already implemented in Phase 1) is the agent's **requested** level. At task execution time, the policy resolver computes the **effective** level from the four-scope hierarchy and stores it on the `AgentTaskJob`.

## Consequences

- Phase 2 task domain code must resolve the effective autonomy level before dispatching any `AgentTaskJob`.
- No agent may self-escalate beyond its effective autonomy level.
- Any PR that allows an agent prompt to override its autonomy level must be rejected.
- Tenant-level autonomy caps must be evaluated before team- or task-level overrides.
