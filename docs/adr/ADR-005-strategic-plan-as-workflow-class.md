# ADR-005 — StrategicPlan Is a WorkflowClass; Cross-Team Sequencing Belongs to the External Orchestrator

**Status:** Accepted  
**Date:** 2026-11-03  
**Deciders:** Architecture  
**Implements:** Phase 3 gate decision (follows ADR-001 through ADR-004)

---

## Context

Phase 2 (ADR-004) gave FlowOS a first-class `AgentTask` step type: a workflow step
where an `AgentTeam` is the sole actor. A `TeamExecution` record tracks each
step's runtime lifecycle.

The remaining gap: there is no first-class object that says "run Team A, then
Team B depending on Team A's outcome, then Team C — and treat the whole thing
as one traceable unit." Two candidate shapes were considered:

**Option A — New `StrategicPlan` aggregate (pure domain model)**  
A new entity under `FlowOS.Domain/Entities/Orchestration/` owns an ordered or
graph-structured list of `TeamExecution` references, carries its own status,
and is orchestrated by a new application service. Cross-team sequencing logic
lives in that service.

**Option B — `StrategicPlan` as a `WorkflowClass` composed of `AgentTask` steps**  
A strategic plan is a `WorkflowClass` whose steps are `AgentTask` nodes. The
engine's existing Fork, Join, Decision, Timer, SLA, and parallel-branch
machinery handles within-plan sequencing.

The Phase 3 gate decision added a third dimension: **the cross-team sequencing
responsibility that Option A would put inside FlowOS can instead be delegated
to an External Orchestrator Agent running outside FlowOS.** That agent talks
to FlowOS exclusively through MCP tools.

---

## Decision

**Option B is adopted for FlowOS internals. Option A's sequencing responsibility
is assigned to the External Orchestrator Agent.**

A `StrategicPlan` is a `WorkflowClass` whose steps are `AgentTask` nodes.
Cross-team sequencing — deciding which team runs next, handling cross-team
dependencies, and replanning after failures — is the responsibility of an
External Orchestrator Agent that calls FlowOS MCP tools.

---

## Architecture: Three Tiers, One Engine, One External Brain

```
┌─────────────────────────────────────────────────────────────┐
│  External Orchestrator Agent  (outside FlowOS)               │
│                                                               │
│  Responsibilities:                                            │
│  • Decide overall strategy and team sequencing               │
│  • React to TeamExecution outcomes and replan                │
│  • Publish gate events to advance the StrategicPlan          │
│  • Never maintain its own copy of plan state                 │
│                                                               │
│  Talks to FlowOS via MCP tools only:                         │
│    start_workflow, publish_event, read_change_feed,          │
│    get_strategic_plan_status, get_team_execution_outcome      │
└──────────────────────────┬──────────────────────────────────┘
                           │ MCP tools only — no internal API
┌──────────────────────────▼──────────────────────────────────┐
│  FlowOS — StrategicPlan WorkflowClass                        │
│                                                               │
│  Steps are AgentTask nodes (one per team role in the plan)   │
│                                                               │
│  Engine owns:                                                 │
│  • Pause / wait per AgentTask step                           │
│  • SLA timers and escalation                                 │
│  • Fork / Join for parallel team execution                   │
│  • AutoCommitEvaluator — agent proposals → state             │
│  • Autonomy policy enforcement (ADR-010)                     │
│  • Immutable audit trail (change feed, execution history)    │
└──────────────────────────┬──────────────────────────────────┘
                           │ creates / updates
┌──────────────────────────▼──────────────────────────────────┐
│  TeamExecution  (Phase 2, ADR-004)                           │
│                                                               │
│  One record per AgentTask step execution                     │
│  Tracks: slot assignments (RoleName → AgentId), status,      │
│  outcome event, confidence score, failure reason             │
└─────────────────────────────────────────────────────────────┘
```

---

## Three Inviolable Rules

### Rule 1 — FlowOS never sequences across teams

FlowOS executes one `AgentTask` step at a time and waits. It does not know or
care what team runs next at the plan level. Cross-team dependencies and
sequencing decisions belong entirely to the External Orchestrator Agent.

### Rule 2 — The External Orchestrator never touches workflow state directly

The orchestrator calls MCP tools only. It never calls internal FlowOS APIs,
never mutates `TeamExecution` records, never invokes `WorkflowEngine` methods,
and never bypasses `AutoCommitEvaluator`. ADR-003 applies unchanged: all agent
decisions are proposals.

### Rule 3 — The StrategicPlan WorkflowClass is the source of truth for plan state

The External Orchestrator reads plan state from FlowOS. It does not maintain its
own copy. If the orchestrator crashes and restarts, it calls
`get_strategic_plan_status` and `read_change_feed` to reconstruct its context,
then resumes from where FlowOS says the plan is.

---

## What FlowOS Provides (Phase 3 Deliverables)

### 1. StrategicPlan WorkflowClass convention

A `WorkflowClass` tagged `planType: strategic` whose steps are `AgentTask`
nodes. The tag is metadata only — the engine treats it as a normal workflow.
The tag lets MCP tools and the change feed emit plan-specific context.

### 2. Enriched TeamExecution outcome on the change feed

When an `AgentTask` step completes, the change feed event is enriched with:

```json
{
  "changeType": "AgentTaskCompleted",
  "workflowInstanceId": "<planInstanceId>",
  "stepId": "<stepId>",
  "teamExecutionId": "<teamExecutionId>",
  "committedEvent": "RISK_LOW",
  "confidence": 0.93,
  "actorAgentId": "Agent:risk-analyst-v2",
  "slotAssignments": { "RiskAnalyst": "Agent:risk-analyst-v2" },
  "completedAtUtc": "2026-11-03T07:00:00Z"
}
```

This is the primary signal the External Orchestrator uses to make its next
sequencing decision.

### 3. New MCP tools

| Tool | Purpose |
|---|---|
| `get_strategic_plan_status` | Returns plan instance status, all step states, and the latest `TeamExecution` outcome per step. Single call to reconstruct orchestrator context after restart. |
| `get_team_execution_outcome` | Returns the full outcome for one `TeamExecution`: committed event, confidence, actor, slot assignments, failure reason. |

### 4. MCP guidance block

A new "StrategicPlan / External Orchestrator law" guidance block in
`FlowOsMcpGuidance.cs` tells AI agents exactly how to design and operate a
multi-team strategic plan. See section below.

---

## What Does NOT Change

- `WorkflowEngine` — no changes. `AgentTask` already works.
- `TeamExecution` aggregate — no changes. Outcome fields are already there.
- `AutoCommitEvaluator` / `AgentTaskCoordinator` — no changes.
- ADR-003 governance — unchanged. No agent bypasses the evaluator.
- ADR-002 table naming — unchanged. `TeamExecutions` ≠ `Teams`.

---

## MCP Guidance Block (to be added to FlowOsMcpGuidance.cs)

```
StrategicPlan / External Orchestrator law (ADR-005 — read before designing
multi-team plans):

Architecture: three tiers.
  Tier 1 — External Orchestrator Agent (outside FlowOS): owns strategy,
    cross-team sequencing, replanning. Talks to FlowOS via MCP tools only.
  Tier 2 — FlowOS StrategicPlan WorkflowClass: AgentTask steps, one per
    team role. Engine handles pause/wait, SLA, fork/join, autonomy policy.
  Tier 3 — TeamExecution (Phase 2): one record per AgentTask step execution.

Three rules that never bend:
  1. FlowOS never sequences across teams. That is the orchestrator's job.
  2. The orchestrator never touches workflow state directly — MCP tools only.
  3. The StrategicPlan WorkflowClass is the source of truth. The orchestrator
     reads state from FlowOS; it does not maintain its own copy.

Designing a StrategicPlan WorkflowClass:
  - Each AgentTask step = one team role in the plan (e.g., DataGathering,
    RiskAssessment, DecisionReview).
  - Set agentTeamId on each step to the AgentTeam template for that role.
  - Use Fork/Join for parallel team execution. Use Decision/nextSteps for
    conditional routing based on a team's committed event.
  - Set SLA on each AgentTask step independently — not on the plan as a whole.
  - Tag the WorkflowClass with planType: strategic (metadata only).

Operating the plan (External Orchestrator loop):
  1. Call start_workflow to create a StrategicPlan instance.
  2. Poll read_change_feed (after = previous cursor). On AgentTaskCompleted:
       a. Call get_team_execution_outcome for the full outcome.
       b. Decide next action: publish a gate event to advance the plan, or
          replan (start a new instance, escalate, abort).
  3. To advance: call publish_event with the gate event the WorkflowClass
     nextSteps expects. Never invent an event — use the legal nextSteps keys.
  4. After a crash/restart: call get_strategic_plan_status to reconstruct
     context, then resume from step 2.

What the orchestrator must NOT do:
  - Do not maintain a local copy of plan state. Always read from FlowOS.
  - Do not publish events that bypass a waiting AgentTask step — let the
    AutoCommitEvaluator decide whether to advance.
  - Do not call run_agent_task on behalf of the plan — AgentTaskCoordinator
    dispatches automatically when the engine enters an AgentTask step.
  - Do not set effectiveAutonomyLevel in the step definition — computed at
    dispatch (ADR-010).

Preferred prompt: design_strategic_plan_workflow.
Preferred resource: flowos://guides/strategic-plan-orchestration.
```

---

## Consequences

- Any `WorkflowClass` with `AgentTask` steps can function as a strategic plan
  without any new FlowOS aggregate or migration.
- The External Orchestrator is a separately deployed component; its
  implementation is out of scope for this ADR.
- Phase 3 scope is bounded: enriched change feed, two new MCP tools,
  MCP guidance, and a `design_strategic_plan_workflow` prompt. No new domain
  entities, no engine changes, no migrations beyond what Phase 2 already
  delivered.
- Phase 4 (Communication + Memory) and Phase 5 (Evaluation + Adaptive
  Execution) are unaffected by this decision.
