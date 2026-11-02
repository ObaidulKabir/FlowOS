# ADR-003 — FlowOS Remains the Execution and Governance Authority

**Status:** Accepted  
**Date:** 2026-11-03  
**Deciders:** Architecture  

## Context

The Agent Team Orchestrator introduces agent-generated decisions (task completions, plan revisions, agent selections). Without a clear authority boundary, an agent could report task completion and have it recorded without the state machine ever running — directly violating the "State Machine = Law" principle.

## Decision

**The Agent Team Orchestrator must never become the authority for business state or workflow execution.**

Its job is to decide *who* should do *what*, in *what order*, and whether the work is *good enough*.

FlowOS remains responsible for whether that work is **permitted**, how it **executes**, and what business **state changes** are valid.

### Mechanism: AgentTask as a workflow step type

`AgentTaskJob` (the existing entity) already models a task executed by an agent on behalf of a workflow step. This is the correct foundation.

**Rule:** An agent-generated decision is a **proposal**, not a state transition.

Concretely:
1. A specialist agent reports that its task is complete.
2. FlowOS (via the existing `AutoCommitPolicy` / `AutoCommitEvaluator`) validates the task contract, required artifact, and evaluation criteria.
3. Only after successful validation does FlowOS commit a `DomainEvent` that advances the workflow state.

**Internal operational states** (`Leased`, `RetryPending`, `ReviewPending`) are metadata on the `AgentTaskJob` entity — not business state transitions governed by the state machine. They track the health of the execution pipeline, not the business outcome.

### Separation from ExternalAgentPlan

`ExternalAgentPlan` (existing) is the execution contract for a **single external agent against a single workflow instance**.

`StrategicPlan` (Phase 3) spans multiple teams and multiple workflow instances. These are permanently separate aggregates:

- `ExternalAgentPlan`: single-agent, single-instance, existing pipeline.
- `StrategicPlan`: multi-team, multi-instance, new aggregate.

No existing `ExternalAgentPlan` code should be extended to serve the `StrategicPlan` role.

### Autonomy enforcement

Autonomy levels (L0–L3, defined in ADR-010) are enforced **independently of the agent's prompt**. The agent cannot negotiate its own permission level. Enforcement is at the FlowOS policy layer:

- L0 (Advisory): FlowOS never auto-commits; all transitions require explicit human approval.
- L1 (Supervised): FlowOS requires `confirmHumanApproval: true` on each action.
- L2 (Governed Autonomous): FlowOS enforces predefined policy boundaries; no per-action approval.
- L3 (Adaptive Autonomous): Agent may replan and delegate; FlowOS validates each replanned task contract before execution.

## Consequences

- New agent task domain code (Phase 2) must model agent decisions as proposals flowing into the existing `AgentTaskJob` / `AutoCommitEvaluator` pipeline.
- No Phase 2 code may introduce a direct state transition on a `WorkflowInstance` that bypasses the `AutoCommitEvaluator`.
- Replanning (Phase 3) must produce a revised `StrategicPlan` or `OperationalPlan` submitted for FlowOS validation — not a self-applied mutation.
- Any PR that introduces a direct workflow state transition from agent code without going through the evaluator pipeline must be rejected.
