# ADR-007 — Closing the Declarative Chain for Agents

**Status:** Proposed
**Date:** 2026-11-04
**Deciders:** Architecture
**Follows:** ADR-005 (cross-team sequencing), ADR-006 (declarative AgentWorkflowClass blueprint), ADR-010 (autonomy is policy-controlled)

---

## Context

More of the agent definition is already declarative than ADR-006's framing suggests. The
problem is not absence of declaration — it is that the declarations do not reference each
other, and no reference is checked.

### What already exists

| Layer | Mechanism | State |
|---|---|---|
| **Individual agent** | `AgentProfileConfiguration` in the plugin binding registry (`PluginBindingTypes.Profile`), bundling `Role`, `ProviderAlias`, `PromptAlias`, `ToolAliases`, `AutoCommitThreshold`, `AllowedEvents` | declared, tenant-scoped, resolved by alias |
| **Published agent** | `AgentVersion` — *"the 'Law' side of the agent — once published it is never mutated, so historical executions can always be replayed against the exact configuration that was active"*, with a typed `AutonomyLevel` and `SatisfiesCapabilities(requiredTags)` | immutable, versioned |
| **Referenced from a workflow** | A `WorkflowClassBlueprint` step names `AgentProvider` / `AgentPrompt` / `AgentTools` aliases; `DecisionPacketBuilder` resolves the provider alias to a profile and merges | wired, human path only |

So an individual agent is the *most* declarative layer, not the least. The chain from a
published workflow step down to a model call already exists and runs.

### The seven gaps

1. **No reference is validated at publish.** `WorkflowClassValidator` mentions
   `AgentPrompt` once, in a non-empty check. A step naming `AgentProvider =
   "claude-risk-analyzr"` publishes successfully and fails at dispatch — inside an
   immutable class that must then be versioned to escape. This is the same class of defect
   as the unvalidated autonomy string, one layer up.

2. **Effective configuration is not derivable from declarations.** Which fields a step
   overrides and which the profile supplies lives in `DecisionPacketBuilder`, with
   `AgentProfileConfiguration.Merge(existing, incoming)` deciding the rest. Reading a
   published blueprint does not tell you what agent will actually run.

3. **"Role" is a free string at four layers that never meet.**
   `AgentProfileConfiguration.Role`, `AgentVersion.Role`, `AgentTeam` slot `roleName`, and
   `AgentStepBlueprint.TeamRole`. All four must agree for dispatch to work; nothing makes
   them agree.

4. **An agent plan step cannot reach a profile.** `AgentStepBlueprint` has `AgentPrompt`
   and `AgentTools` but **no `AgentProvider`** — and the provider alias is what triggers
   profile resolution. So the agentic path holds the two weaker halves of the reference and
   re-declares what a profile already bundles.

5. **No team blueprint.** `AgentTeam` has no public write path at all: no controller, no
   MCP tool, no command or handler, and `IAgentTeamRepository.AddAsync` / `UpdateAsync`
   are called by nothing.

6. **An agent plan cannot express a human gate.** `AgentStepType` is
   `Agent | Fork | Join | End`; the step record has no `Actor`, `Role` or
   `RequiredCapabilities`. `Consequential = true` declares that an agent must not commit a
   step while providing no way to name who decides instead.

7. **Separation of duties cannot be declared.** `RoleBlueprint` resolves *who holds* a
   role; nothing expresses relationships *between* assignments. `Consequential` plus
   "anyone holding the capability" permits the approver to be whoever created the work.

### How these were found

Five *expectation-first* declarations were written — the blueprint one would want for a real
application, constrained by nothing — and each gap taken from the diff against what exists.
A requirement was recorded only once an example demanded it.

| Case | Dimension probed |
|---|---|
| Supplier due diligence | Parallel specialists on disjoint subproblems; consequential money gate |
| Quarterly access review | Recurring schedule; dynamic fan-out; high-volume auto-commit |
| Support ticket triage | Tight SLA with reminders; severity branching; loop caps |
| Deployment change review | Agents requiring *reconciliation*; compensation for completed work |
| Written answer marking | Same role twice with addressable outputs; numeric-tolerance agreement; separation of duties; sampling |

Three candidates were discarded on inspection, recorded here so they are not re-proposed:

- **A declared rubric** belongs in a context binding, not a blueprint.
- **Conditional consequentiality** needs no schema change — splitting on a `Decision` step
  expresses it. Recorded as an idiom.
- **A new agent-definition record** would duplicate `AgentProfileConfiguration`. An earlier
  draft of this ADR proposed exactly that and was wrong.

---

## Decision

**One definition per concept, referenced by alias, every reference validated at publish, and
the effective configuration derivable from declarations alone.**

### 1. The chain and its single owner per link

```
plugin binding registry ──┬── profile → AgentProfileConfiguration   the agent
                          ├── prompt                                 its instructions
                          ├── action                                 its tools
                          └── agent/decision                         providers
                                     │ published as
                                     ▼
                              AgentVersion            immutable, typed AutonomyLevel
                                     │ claims
                                     ▼
                              AgentRole               the declared join key
                                     │ seated by
                                     ▼
                         AgentTeamBlueprint slots      role + required capabilities
                                     │ requested by
                                     ▼
                    AgentWorkflowBlueprint step        names a role, nothing else
```

A step names a **role**. It does not name a prompt, a provider or a tool list — those
belong to the profile, declared once.

### 2. Publish-time closure

A blueprint publishes only if every reference resolves:

- every step's `AgentRole` is seated by a slot in the referenced team;
- every slot's `RequiredCapabilities` is satisfiable by at least one profile claiming that
  role (`AgentVersion.SatisfiesCapabilities` already answers this);
- every profile's `ProviderAlias`, `PromptAlias` and `ToolAliases` resolve in the plugin
  binding registry;
- every `HumanTask` step's `Role` is declared in `Roles` and its capabilities in
  `Capabilities`.

This closure applies to **both** paths. `WorkflowClassBlueprint` gains the same alias
validation, which fixes gap 1 for the human path that is live today.

### 3. Effective configuration is computable, not merged in code

The profile is authoritative. A step may **narrow** — fewer tools, a lower autonomy ceiling
— and may never substitute a different provider or prompt. Narrowing is expressible in the
declaration; substitution is not.

Consequently the effective agent for any step is derivable by reading declarations, which is
what makes the chain *declarative* rather than merely configured. `AgentProvider` and
`AgentPrompt` on `AgentStepBlueprint` are therefore **not** added; `AgentTools` is retained
as a narrowing filter over the profile's tools.

### 4. Autonomy composes downward only

```
effective = min(tenant setting, profile/AgentVersion level, team ceiling, step override)
```

A later link may restrict; it may never widen. `AgentTeam` already documents
override-downward-only, but with four levels the rule must be stated or a union will be
implemented by accident. The value of a step's override is validated against the enum as of
the preceding change; the *direction* becomes checkable once a step's role resolves to a
team and a profile.

### 5. The ADR-005 boundary is reframed, not withdrawn

FlowOS does not *decide* cross-team sequencing; it *constrains* what sequencing is
permissible. Both paths coexist — internal dispatch once Phase 4B exists, and external
orchestrators bound by a contract — and both MUST compile their declarations to `Policy`
records evaluated through the existing `IPolicyEvaluator` / `PolicyEnforcementBehavior`. Two
authoring surfaces, one enforcement point. If either grows its own check, work routes to
whichever constrains less and the guarantee is void.

### 6. FlowOS is the only path to governed systems

**Granting an agent direct credentials to a governed system voids this ADR's guarantee for
that tenant.** Stated as a voiding condition because it is a deployment property, not a code
property, and is the assumption most easily eroded in production. Agent API keys SHOULD be
scoped so they are unusable outside FlowOS.

### 7. Deny by default

- **Teams and roles: universally.** `AgentTeam` has no public write path, so there is no
  imperatively-created population to grandfather and no breaking change.
- **External agent contracts: newly registered profiles only.** An existing external agent
  has no declared contract, and synthesising one would invent permissions.

### 8. The team blueprint is mandatory

An optional blueprint is documentation; a declaration that imperative edits can bypass is
not a boundary. `AgentTeamBlueprint` is the only way a team comes into being, and
`AgentTeam` becomes compiler output, mirroring
`WorkflowClassBlueprint → WorkflowClassCompiler → WorkflowStepDefinition`.

Coexistence applies at the **team** level, never the **field** level: there is no mode in
which a declared team is also imperatively editable. This follows the existing precedent
that a class-backed workflow instance is governed by its compiled class and fails closed.

---

## Architecture

### New records

```
AgentTeamBlueprint                          (new blueprint, versioned + publish-immutable)
  ├── Name, Objective
  ├── MaxAutonomyLevel: AgentAutonomyLevel   team ceiling
  └── Slots: AgentTeamSlotBlueprint[]
        ├── RoleName: string                 declares the role vocabulary
        ├── RequiredCapabilities: string[]
        └── DefaultProfileAlias: string?     a plugin binding of type 'profile'

AgentWorkflowBlueprint                       (existing — additions)
  ├── Capabilities: CapabilityBlueprint[]    reuse existing record
  ├── Events: EventBlueprint[]               reuse existing record
  ├── Roles: RoleBlueprint[]                 reuse existing record — human gates
  ├── Team: string                           an AgentTeamBlueprint name
  ├── Constraints: AssignmentConstraint[]    separation of duties
  └── Steps[]
        ├── StepType: Agent | Fork | Join | End | HumanTask
        ├── AgentRole: string?               replaces TeamRole; resolves to a team slot
        ├── Role: string?                    HumanTask: a declared Roles entry
        ├── Actor: Human | Agent | Either    reuse StepActor
        ├── RequiredCapabilities: string[]   narrowing only
        └── AgentTools: string[]             narrowing filter over the profile's tools

AssignmentConstraint                         (new)
  ├── Rule: "distinct" | "notSubjectOf" | "seniorTo"
  ├── Between: string[]                      "Marker#0", "Marker#*", "moderate.assignee"
  └── Subject: string?                       context expression
```

`TeamRole` is retained as a deprecated alias for one release so existing Phase 4A drafts
keep validating, and is rejected on publish once `AgentRole` is available.

Constraints are validated structurally at publish — do the named roles and steps exist — and
evaluated at assignment time, the only moment actual holders are known.

### Error code prefixes

`AWF-` (agent workflow) is established. `ATM-` for `AgentTeamBlueprint`, `EAC-` for the
external agent contract. Alias-resolution failures take `AWF-REF-*` and, on the human path,
`WF-REF-*`.

---

## What does NOT change

- `AgentProfileConfiguration`, `AgentVersion` and the plugin binding registry — the agent
  definition is already right and is not re-declared.
- `WorkflowClassBlueprint` semantics — it gains alias validation only.
- `WorkflowEngine` — untouched. Agent plans still do not dispatch; that is Phase 4B.
- `Team` (human IAM) — ADR-002's separation from `AgentTeam` is unaffected.
- The MCP tool surface — no new tools; guidance updates with each behaviour.

---

## Successor ADRs

This ADR closes the declaration chain. Nothing else proceeds until it does, because every
successor references roles, capabilities or profiles that must first resolve.

| Cluster | Content | Depends on |
|---|---|---|
| **ADR-007** *(this)* | Chain closure: role vocabulary, team blueprint, reference validation, human gate, duty separation, autonomy composition | — |
| **ADR-008** | Data flow: step output provenance, instance-qualified addressing, `Decision` + conditions, `ContextSchema` enforced at handoff | 007 |
| **ADR-009** | Execution model: `ForEach` fan-out, `Timer`/schedule, consensus join, saga compensation, idempotency, sampling assurance | 008 |
| **ADR-011** | Vocabulary parity: SLA reminders, step priority, `OnFailure` hooks ported from `StepBlueprint` | none |

`Decision` + conditions sits in ADR-008, not here: conditions have nothing to read until
output provenance is declared, so shipping the step type first yields an unusable construct.
`ForEach` is deferred because it changes the execution model and raises questions none of
the five cases answer — partial-failure tolerance, per-item aggregation, what
`MaxGlobalRetries` means with hundreds of items in flight, and what a fan-out of three
hundred renders as.

(ADR-010 is taken by the autonomy-policy decision, so vocabulary parity takes the next free
number.)

---

## Consequences

- A published blueprint can no longer reference an agent, prompt, tool or role that does not
  exist. The largest current class of defect — valid blueprint, broken at dispatch — is
  eliminated for both paths.
- The effective agent for any step becomes readable from declarations rather than from
  `DecisionPacketBuilder`.
- `Consequential` becomes operative rather than decorative: a plan can name who decides.
- Separation of duties becomes declarable, closing a hole that applies to every consequential
  step already shipped.
- Agent teams gain versioning and lineage, so an auditor can be shown which team definition
  governed a given execution.
- `AgentTeam` loses its imperative write path. Costless today because it was never exposed,
  but team authoring must arrive *with* the compiler rather than after it.
- Four blueprints will exist. Their validators must share structure or they will drift.
- Alias validation will reject blueprints that currently publish. Intended, but it means the
  human-path change needs a migration pass over stored classes to find references that no
  longer resolve.

### Open decision for review

`AgentTeam` carries its own `AgentTeamStatus` (`Draft` / `Active` / `Archived`) with
`Activate()` and `Archive()`. If `AgentTeamBlueprint` owns a `Draft` → `Published` →
`Deprecated` / `Abandoned` lifecycle, one concept has two lifecycles and they will disagree.

Recommendation: the blueprint's lifecycle is authoritative and the entity's status becomes
derived, with `Activate` / `Archive` becoming compiler-internal. The alternative is correct
only if a published team should be withdrawable from service without deprecating its
declaration. This must be settled before `AgentTeamBlueprint` is implemented.
