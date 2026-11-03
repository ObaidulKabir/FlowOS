# ADR-007 — Declarative Agent Roles, Human Gates and Separation of Duties

**Status:** Proposed
**Date:** 2026-11-04
**Deciders:** Architecture
**Follows:** ADR-005 (cross-team sequencing), ADR-006 (declarative AgentWorkflowClass blueprint)

---

## Context

ADR-006 made the agent *coordination plan* declarative. It did not make the *agent* declarative.

ADR-006's own Context section states the problem it set out to solve: *"an AgentTeam is an
imperative object — its coordination rules, routing, autonomy constraints, and retry policy
live as C# properties with no immutable blueprint record."* Phase 4A addressed the plan and
left `AgentTeam` untouched, so half the stated motivation remains open.

The gap is not cosmetic. Three findings make it concrete:

1. **`AgentWorkflowBlueprint` cannot express a human gate.** `AgentStepType` is
   `Agent | Fork | Join | End`, and `AgentStepBlueprint` has no `Actor`, no `RequiredRoles`
   and no `RequiredCapabilities`. So `Consequential = true` declares "an agent must not
   commit this" while providing no way to say who decides instead. The governance primitive
   exists with nothing to hand off to.

2. **A step's role binding resolves against nothing.** `AgentStepBlueprint.TeamRole` is a
   free string, referenced in exactly two places in `src/`: its own declaration, and a
   non-empty check in `AgentWorkflowBlueprintValidator`. No registry, no vocabulary, no
   lookup. (`TeamEscalationService.TryEscalateTeamRolesAsync` is unrelated — it escalates
   human `Team` roles, which ADR-002 keeps separate.)

3. **The autonomy ceiling is the least-validated field in the blueprint.**
   `AgentTeam.MaxAutonomyLevel` is a typed `AgentAutonomyLevel`
   (`Advisory` / `Supervised` / `GovernnedAutonomous`). `AgentStepBlueprint.MaxAutonomyLevel`
   is an unvalidated `string?` documented as overriding the team's level "when more
   restrictive" — with nothing validating the value and nothing enforcing the direction.

`AgentWorkflowBlueprint` is also described in ADR-006 as mirroring `WorkflowClassBlueprint`.
It mirrors one of its five sections. `WorkflowClassBlueprint` declares `Events`,
`StateMachine`, `Workflow`, `Roles` and `Capabilities`; the agent blueprint has the
`Workflow` equivalent plus `ContextSchema`, and adds `CoordinationRules`. `Events`,
`Roles` and `Capabilities` have no counterpart.

### How these requirements were derived

Rather than designing a schema and checking what it could express, five *expectation-first*
declarations were written — the blueprint one would want for a real application, constrained
by nothing — and the architecture requirement taken from the diff against what exists. The
method is specification by example; the discipline is that a requirement is only recorded
once an example demands it.

| Case | Dimension it probed |
|---|---|
| Supplier due diligence | Parallel specialists on disjoint subproblems; consequential money gate |
| Quarterly access review | Recurring schedule; dynamic fan-out over a collection; high-volume auto-commit |
| Support ticket triage | Tight SLA with reminders; severity branching; loop caps; human takeover |
| Deployment change review | Agents that must be *reconciled* rather than merged; compensation for completed work |
| Written answer marking | Same role run twice with addressable outputs; numeric-tolerance agreement; separation of duties; sampling |

Two of the twenty requirements the exercise produced were discarded on inspection, which is
recorded here so they are not re-proposed:

- **A declared rubric** is not a blueprint concern. `WorkflowContextBinding` is already
  tenant-scoped, typed, named and revisioned with a draft/active lifecycle, which is the
  correct home for domain reference data.
- **Conditional consequentiality** (varying `Consequential` by output value) needs no schema
  change. Splitting on a `Decision` step expresses it — a failing grade routes to a
  consequential step, a passing grade does not. This is recorded as an idiom, not a feature.

---

## Decision

### 1. The ADR-005 boundary is reframed, not withdrawn

FlowOS does not *decide* cross-team sequencing; it *constrains* what sequencing is
permissible. An orchestrator proposes, the substrate validates against a declaration and
admits or refuses.

Both execution paths will coexist: declared plans dispatched internally once the Phase 4B
engine exists, and external orchestrators bound by a contract.

**Constraint:** both paths MUST compile their declarations to `Policy` records and evaluate
them through the existing `IPolicyEvaluator` / `PolicyEnforcementBehavior`. Two authoring
surfaces, one enforcement point. If either path grows its own check, work will route to
whichever path constrains less, and the guarantee is void.

### 2. FlowOS is the only path to governed systems

The declarative boundary covers effects *through* FlowOS. An agent holding direct
credentials to a governed system — a database, an ERP, its own connectors — produces effects
that never reach the policy gate.

**Granting an agent direct credentials to a governed system voids this ADR's guarantee for
that tenant.** This is stated as a voiding condition rather than a recommendation because it
is a deployment property, not a code property, and is the assumption most easily eroded in
production. Agent API keys SHOULD be scoped so they are unusable outside FlowOS.

### 3. Deny by default

A proposal that no declaration covers is refused.

- **Teams: universally.** `AgentTeam` has no public write path today — no controller, no MCP
  tool, no command or handler, and `IAgentTeamRepository.AddAsync` / `UpdateAsync` are called
  by nothing. There are no imperatively-created teams to grandfather, so enforcement applies
  from the start with no breaking change.
- **External agent contracts: newly registered profiles only.** An existing external agent
  has no declared contract, and one cannot be synthesised safely because doing so would
  invent permissions.

### 4. The blueprint is mandatory; `AgentTeam` becomes compiled output

An optional blueprint is documentation. A declaration that imperative edits can bypass is
not a governance boundary.

`AgentTeamBlueprint` is the only way a team comes into being. `AgentTeam` becomes compiler
output, mirroring `WorkflowClassBlueprint` → `WorkflowClassCompiler` → `WorkflowStepDefinition`.
`AddAsync` and `UpdateAsync` become compiler-internal.

Coexistence applies at the **team** level, never the **field** level: there is no mode in
which a declared team is also imperatively editable. This follows the existing precedent that
a class-backed workflow instance is governed by its compiled class and fails closed on
violation.

---

## Architecture

### Three blueprint records, one enforcement point

| Declares | Blueprint | Compiles to |
|---|---|---|
| Team composition, member slots, autonomy ceiling | `AgentTeamBlueprint` | `AgentTeam` + `Policy` |
| Coordination plan *(exists)* | `AgentWorkflowBlueprint` | 4B dispatcher input + `Policy` |
| What an external agent may propose | `ExternalAgentContractBlueprint` | `Policy` |

All three are versioned, published-immutable and lineage-tracked, following `WorkflowClass`.
All three are enforced by the evaluator that already exists; no new enforcement code.

### Agent roles become declared

```
AgentWorkflowBlueprint
  ├── Capabilities: CapabilityBlueprint[]      (new — reuse existing record)
  ├── Events: EventBlueprint[]                 (new — reuse existing record)
  ├── AgentRoles: AgentRoleBlueprint[]         (new)
  │     ├── RoleName: string                   (what a step's AgentRole resolves to)
  │     ├── Team: string                       (an AgentTeamBlueprint name)
  │     ├── RequiredCapabilities: string[]
  │     ├── MaxAutonomyLevel: AgentAutonomyLevel
  │     ├── Prompt: string?
  │     └── Tools: string[]
  ├── Roles: RoleBlueprint[]                   (new — reuse existing record, human gates)
  ├── Constraints: AssignmentConstraint[]      (new — separation of duties)
  └── Steps: AgentStepBlueprint[]
        ├── StepType: Agent | Fork | Join | End | HumanTask | Decision
        ├── AgentRole: string?                 (replaces free-string TeamRole)
        ├── Role: string?                      (HumanTask: a declared Roles entry)
        ├── RequiredCapabilities: string[]     (new — per-step narrowing)
        └── Actor: Human | Agent | Either      (new — reuse StepActor)
```

`TeamRole` is retained as a deprecated alias for one release so existing Phase 4A drafts keep
validating, and is rejected on publish once `AgentRole` is available.

### Autonomy composes downward only

Three ceilings now exist. The effective level is their intersection:

```
effective = min(tenant setting, team blueprint, step override)
```

A step may restrict further; it may never exceed. `AgentTeam` already documents
override-downward-only, but with three levels the rule must be stated explicitly or a union
will be implemented by accident.

### Separation of duties

`RoleBlueprint` resolves *who holds* a role. Nothing expresses *relationships between*
assignments, and the governance claim is weaker than the system implies: `Consequential`
plus "anyone with the capability" permits the approver to be the person who created the work.

```
AssignmentConstraint
  ├── Rule: "distinct" | "notSubjectOf" | "seniorTo"
  ├── Between: string[]     ("Marker#0", "Marker#*", "moderate.assignee")
  └── Subject: string?      (context expression, e.g. "{{studentId}}")
```

Constraints are validated structurally at publish (do the named roles and steps exist) and
evaluated at assignment time, which is the only moment the actual holders are known.

---

## What does NOT change

- `WorkflowClassBlueprint` and the human workflow path — untouched.
- `WorkflowEngine` — untouched. Agent plans still do not dispatch; that is Phase 4B.
- The MCP tool surface — no new tools. Guidance is updated in the same PR as each behaviour.
- `Team` (human IAM) — ADR-002's separation between `Team` and `AgentTeam` is unaffected.

---

## Successor ADRs

The twenty requirements cluster into four, and the dependency order is forced. This ADR is
the first cluster only.

| Cluster | Content | Depends on |
|---|---|---|
| **ADR-007** *(this)* | Agent roles, step→role binding, human gate, separation of duties | — |
| **ADR-008** | Data flow: step output provenance (`Writes`), instance-qualified addressing, `Decision` conditions, `ContextSchema` enforced at handoff | 007 |
| **ADR-009** | Execution model: `ForEach` dynamic fan-out, `Timer`/schedule, consensus join, saga compensation, idempotency keys, sampling-based assurance | 008 |
| **ADR-011** | Vocabulary parity: SLA reminders, step priority, `OnFailure` hooks ported from `StepBlueprint` | none — may land anytime |

(ADR-010 is already taken by the autonomy-policy decision, so the vocabulary cluster takes
the next free number.)

`Decision` + `Conditions` is listed under ADR-008 rather than here because conditions have
nothing to read until output provenance is declared. Shipping the step type first would
produce a construct that cannot be used.

`ForEach` is deferred deliberately: it changes the execution model rather than the
vocabulary, and raises questions none of the five cases answer — partial-failure tolerance,
per-item result aggregation, what `MaxGlobalRetries` means with hundreds of items in flight,
and what a fan-out of three hundred renders as on the dashboard.

---

## Consequences

- `Consequential` becomes operative rather than decorative: a plan can name who decides.
- A step's role, capabilities and autonomy ceiling are checkable at publish rather than
  discovered at dispatch.
- Agent teams gain versioning, lineage and publish-immutability, so an auditor can be shown
  which team definition governed a given execution.
- Separation of duties becomes declarable, closing a governance hole that applies to every
  consequential step already shipped.
- `AgentTeam` loses its imperative write path. This costs nothing today because that path was
  never exposed, but it does mean team authoring must arrive with the compiler rather than
  after it.
- Four blueprints will exist (`WorkflowClass`, `AgentWorkflow`, `AgentTeam`,
  `ExternalAgentContract`). The validators must share structure or they will drift; the
  `AWF-` code prefix convention extends to `ATM-` and `EAC-`.

### Open decision for review

`AgentTeam` carries its own `AgentTeamStatus` (`Draft` / `Active` / `Archived`) with
`Activate()` and `Archive()`. If `AgentTeamBlueprint` owns a `Draft` → `Published` →
`Deprecated` / `Abandoned` lifecycle, one concept has two lifecycles and they will disagree.

The recommendation is that the blueprint's lifecycle is authoritative and the entity's status
becomes derived, with `Activate` / `Archive` becoming compiler-internal. The alternative is
that publication and operational availability are deliberately distinct — which is correct
only if a published team should be removable from service without deprecating its
declaration. This needs deciding before `AgentTeamBlueprint` is implemented.
