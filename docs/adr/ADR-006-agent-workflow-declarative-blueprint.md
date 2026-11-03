# ADR-006 — Declarative AgentWorkflowClass Blueprint for Multi-Agent Coordination Plans

**Status:** Accepted  
**Date:** 2026-11-03  
**Deciders:** Architecture  
**Follows:** ADR-005 (StrategicPlan as WorkflowClass)

---

## Context

ADR-005 established that a StrategicPlan is a `WorkflowClass` whose steps are
`AgentTask` nodes, and that an External Orchestrator Agent sequences across teams
via MCP tools.

This works for moderate plans, but the agentic layer lacks the same first-class
declarative structure that exists for human workflows.  Today an `AgentTeam` is
an imperative object — its coordination rules, routing, autonomy constraints,
and retry policy live as C# properties with no immutable blueprint record.
Consequences:

1. **No observability of intent.**  You can see what an agentic execution *did*
   but not what the plan *was designed to do*.  There is no declared graph to
   render alongside the execution trace.

2. **No versioning of plan structure.**  When an agentic plan runs for days and
   the team composition changes, there is no record of which version governed
   which execution.

3. **AI agents cannot read the plan structure.**  The External Orchestrator
   calls `get_strategic_plan_status` but has no structured view of the declared
   step graph, routing conditions, SLA, and autonomy constraints *before* it
   starts.  It must re-derive this from code or documentation.

4. **No visual authoring path.**  The `WorkflowClassBlueprint` already has a
   canvas, compiler, and validator.  An agentic plan cannot reuse any of this
   tooling.

---

## Decision

Introduce `AgentWorkflowClass` — a new domain entity that stores an immutable
`AgentWorkflowBlueprint` as its `Definition`.

The structure mirrors `WorkflowClass` → `WorkflowClassBlueprint` exactly:

| Human workflows | Agentic workflows |
|---|---|
| `WorkflowClass` | `AgentWorkflowClass` |
| `WorkflowClassBlueprint` | `AgentWorkflowBlueprint` |
| `StepBlueprint` | `AgentStepBlueprint` |
| `WorkflowInstance` *(existing)* | `AgentWorkflowExecution` *(Phase 4B)* |

---

## Architecture

### Blueprint record tree

```
AgentWorkflowBlueprint
  ├── Objective: string
  ├── StartStepId: string
  ├── ContextSchema: string?           (JSON Schema for shared working memory)
  ├── CoordinationRules: AgentCoordinationRulesBlueprint
  │     ├── MaxGlobalRetries: int
  │     ├── RollbackStrategy: "Abandon" | "Compensate"
  │     ├── GlobalSla: string?         (ISO 8601 duration)
  │     └── MaxParallelSteps: int
  └── Steps: AgentStepBlueprint[]
        ├── StepId: string
        ├── Description: string
        ├── AgentTeamId: Guid?         (static binding)
        ├── TeamRole: string?          (dynamic binding — resolved by orchestrator)
        ├── NextSteps: dict            (event → next step id)
        ├── Branches / JoinPolicy / InboundSteps   (fork/join)
        ├── MaxAutonomyLevel: string?
        ├── Consequential: bool        (never auto-committed)
        ├── AutoCommit: AgentStepAutoCommitBlueprint?
        ├── Sla: AgentStepSlaBlueprint?
        ├── AgentPrompt: string?
        ├── AgentTools: string[]
        └── PathLimits: dict           (retry/resubmit caps)
```

### Entity lifecycle

```
Draft ──publish──▶ Published ──deprecate──▶ Deprecated
  │                                               │
  └──delete (no executions)            ──abandon──▶ Abandoned
```

Identical to `WorkflowClass` lifecycle.  Immutable after publish.

### Storage

`Definition` is stored as `jsonb` in `AgentWorkflowClasses`, serialised by
`AgentWorkflowClassConfiguration` — no `HasDefaultValue`, matching the existing
`WorkflowClassConfiguration` pattern.

---

## What Does NOT Change (Phase 4A)

- `WorkflowEngine` — no changes
- `AgentTeam` / `TeamExecution` — no changes
- MCP tools — no changes
- No new application command/query handlers
- No new execution machinery

Phase 4A is **blueprint data model only** — the entity is stored and versioned;
nothing dispatches from it yet.

---

## Subsequent Phases

**Phase 4B — Blueprint-driven execution**
- `AgentWorkflowExecution` entity (mirrors `WorkflowInstance`)
- Engine reads `AgentWorkflowBlueprint.Steps` to dispatch `TeamExecution`s in
  order, respecting `NextSteps` routing, `Sla`, and `CoordinationRules`
- `get_agent_workflow_blueprint` and `get_agent_workflow_execution` MCP tools

**Phase 4C — Observability**
- MCP guidance block teaching agents to read the blueprint before acting
- Change feed enriched with declared-vs-actual step info
- `design_agent_workflow` MCP prompt (mirrors `design_strategic_plan_workflow`)

---

## Consequences

- Any future multi-agent coordination plan can be declared as an
  `AgentWorkflowClass` without a new aggregate or migration.
- The same visual canvas and compiler used for `WorkflowClass` can be extended
  to author `AgentWorkflowBlueprint`s.
- The External Orchestrator Agent (ADR-005) can call
  `get_agent_workflow_blueprint` (Phase 4B) to reconstruct full plan context
  after a crash, removing the need to re-derive plan structure from code.
- ADR-005's three inviolable rules are unchanged: FlowOS never sequences across
  teams, the orchestrator uses MCP tools only, and the plan is the source of
  truth.
