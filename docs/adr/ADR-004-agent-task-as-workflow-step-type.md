# ADR-004 — AgentTask Is a First-Class Workflow Step Type

**Status:** Accepted  
**Date:** 2026-11-03  
**Deciders:** Architecture  
**Implements:** ADR-003 mechanism decision

## Context

ADR-003 locked the governance mechanism: agent decisions are proposals, not state transitions.
The implementation question was: should `AgentTask` be modelled as a new `HumanTask`-style
workflow step type, or as a separate aggregate with a validation bridge into `AutoCommitEvaluator`?

The Phase 2 gate decision answered: **new HumanTask-style workflow step type**.

## Decision

`AgentTask` is added to the `WorkflowStepType` enum as a first-class value alongside
`HumanTask`, `Command`, `Timer`, etc.

### Semantics

| Property | HumanTask | AgentTask |
|---|---|---|
| Actor | Human (inbox, role-gated) | AI agent (team-based, capability-gated) |
| Pause behaviour | `instance.Wait()` | `instance.Wait()` — identical |
| Completion path | Human submits event | Agent submits proposal → `AutoCommitEvaluator` |
| Direct state transition | No — FlowOS governs | No — FlowOS governs (ADR-003) |
| Step definition fields | `AllowedRoles`, `RequiredCapabilities` | `AgentTeamId`, `AgentTeamSlotRole`, `EffectiveAutonomyLevel` |

Both step types: pause the engine, wait for an external actor, and require an event to be
submitted before the engine advances. The engine treats them identically in Fork/Join and
parallel-branch contexts.

### Step definition additions

Three new optional fields on `WorkflowStepDefinition`:

- `AgentTeamId` (`Guid?`): the `AgentTeam` template to instantiate. Required for `AgentTask`; ignored for all others.
- `AgentTeamSlotRole` (`string?`): the `AgentTeamSlot.RoleName` performing this step. Defaults to the first required slot if absent.
- `EffectiveAutonomyLevel` (`string?`): computed at dispatch by the autonomy policy resolver (ADR-010). Never designer-set.

### AgentTaskScheduling

When `IsWaitingStep` returns true for an `AgentTask` step, `AgentTaskScheduling` encodes the
team+slot as the `RequestedAgentId` in the format `"team:<teamId>/<slotRole>"`. The downstream
coordinator resolves this to an actual agent via `IAgentTeamRepository`.

### TeamExecution

A `TeamExecution` record is created for each `AgentTask` step execution:

- Definition (template): `AgentTeam` (Phase 1)
- Runtime instance: `TeamExecution` (Phase 2) — table `TeamExecutions`
- Tracks slot assignments (`RoleName → AgentId`) and overall status
- Enforces one non-terminal execution per workflow step via unique index

## Consequences

- `WorkflowEngine` now treats `AgentTask` identically to `HumanTask` at the engine layer.
- `DecisionPacketFactory.IsWaitingStep` returns `true` for `AgentTask`.
- Any code that checks `StepType == HumanTask` to decide whether to pause must also check `AgentTask`.
- MCP schema and guidance must document `AgentTask` as a valid step type.
- No agent code may call `instance.AdvanceTo` or `instance.Complete` directly — the `AutoCommitEvaluator` pipeline is the only permitted path (ADR-003).
