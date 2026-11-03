# ADR-007 — Closing the Declarative Chain for Agents

**Status:** Proposed (amended)
**Date:** 2026-11-04 — amended 2026-11-04
**Deciders:** Architecture
**Follows:** ADR-005 (cross-team sequencing), ADR-006 (declarative AgentWorkflowClass blueprint), ADR-010 (autonomy is policy-controlled)

> **Amendment note.** The original decision below treated `AgentProfileConfiguration` —
> tenant-scoped in full — as "the agent definition, already right, not re-declared." That is
> wrong, and it is wrong for a reason the original text already half-states in gap 1 and the
> binding-portability problem raised separately: a tenant-scoped row cannot be what a
> published, portable blueprint means by "its agent." [Decision §1 and §9](#decision) and
> [What does NOT change](#what-does-not-change) are corrected; the rest of the ADR is
> unchanged and is left as originally decided.

---

## Context

More of the agent definition is already declarative than ADR-006's framing suggests. The
problem is not absence of declaration — it is that the declarations do not reference each
other, no reference is checked, and (per the amendment) the one piece that must be portable
across tenants was declared in the one place that cannot travel.

### What already exists

| Layer | Mechanism | State |
|---|---|---|
| **Individual agent** | `AgentProfileConfiguration` in the plugin binding registry (`PluginBindingTypes.Profile`), bundling `Role`, `ProviderAlias`, `PromptAlias`, `ToolAliases`, `AutoCommitThreshold`, `AllowedEvents` | declared, **tenant-scoped in full — this is the defect the amendment corrects** |
| **Published agent** | `AgentVersion` — *"the 'Law' side of the agent — once published it is never mutated, so historical executions can always be replayed against the exact configuration that was active"*, with a typed `AutonomyLevel` and `SatisfiesCapabilities(requiredTags)` | immutable, versioned, **also tenant-scoped today** (`AgentVersion.TenantId`) |
| **Referenced from a workflow** | A `WorkflowClassBlueprint` step names `AgentProvider` / `AgentPrompt` / `AgentTools` aliases; `DecisionPacketBuilder` resolves the provider alias to a profile and merges | wired, human path only |

So an individual agent is the *most* declarative layer, not the least. The chain from a
published workflow step down to a model call already exists and runs — but every link in
that chain, including the ones that have nothing to do with a tenant's infrastructure, lives
in tenant-scoped storage. §9 below is why that is a scoping defect, not just a validation gap.

### The gaps *(seven originally; an eighth added by amendment)*

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

8. **(Added by amendment) The agent definition is scoped to the wrong thing.**
   `AgentProfileConfiguration.Role`, `.PromptAlias`, `.AutoCommitThreshold`, and
   `.AllowedEvents` describe *what the agent is* — they contain nothing tenant-specific.
   But they are stored, resolved, and uniqueness-checked per tenant, alongside
   `.ProviderAlias` and `.ToolAliases`, which genuinely are tenant infrastructure (a
   tenant's own API keys, a tenant's own connector credentials). One record mixes a
   portable definition with a local resolution, so:
   - a `Public`-scope `AgentWorkflowClass` cannot carry its own agents the way it carries
     its own steps; it can only name aliases that may or may not exist wherever it is
     installed (this is the binding-portability problem, now understood as a scope error
     rather than a missing manifest);
   - two tenants running "the same" published blueprint can silently run different agents,
     because nothing stops either tenant's profile alias from drifting after publish —
     there is no immutable, shared copy of *what the agent is* for both to drift away from;
   - publish-time closure (§2) cannot make a durable guarantee: resolving `AgentRole` to a
     profile alias at publish only proves the alias existed in the publishing tenant at
     that moment, not that the agent is stable or that it exists at all in any other tenant
     the class is later shared or copied into.

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
  **(Superseded by amendment — see §9.)** That verdict held only because
  `AgentProfileConfiguration` was assumed to already be the right definition at the right
  scope. It is the right *bundle of fields*; it is the wrong *scope* for roughly half of
  them. The amendment does not reintroduce a duplicate record — it splits the one record
  that conflated two scopes into one definition at each scope, which is a narrower move than
  what was rejected here.

---

## Decision

**One definition per concept, referenced by alias, every reference validated at publish, and
the effective configuration derivable from declarations alone.**

### 1. The chain and its single owner per link *(amended — see §9 for the scope split)*

```
AgentWorkflowBlueprint (application-scoped, immutable once published)
  └── AgentDefinitionBlueprint        role, prompt, autonomy ceiling,       the agent,
        (declared inline, no alias)    required tool/provider CAPABILITIES  portable
                   │ requires (capability, not alias)
                   ▼
tenant resolution, per installing tenant ────────────────────────────────  the binding
  ├── plugin binding registry: agent/decision → concrete LLM provider+model+credentials
  └── plugin binding registry: action        → concrete tool/connector credentials
                   │ compiled together at activation, one tenant at a time
                   ▼
            AgentVersion              immutable per (tenant, class version),
                                      typed AutonomyLevel, SatisfiesCapabilities(tags)
                   │ claims
                   ▼
            AgentRole                 the declared join key
                   │ seated by
                   ▼
       AgentTeamBlueprint slots       role + required capabilities
                   │ requested by
                   ▼
  AgentWorkflowBlueprint step         names a role, nothing else
```

A step names a **role**. It does not name a prompt, a provider or a tool list — those
belong to the agent definition, declared once, inside the same blueprint pack as the steps
that use it — not to a tenant-scoped alias.

### 2. Publish-time closure *(amended — structural closure now, readiness is separate)*

Publish validates **structure**, which is tenant-independent and must hold for every
installing tenant identically:

- every step's `AgentRole` is seated by a slot in the referenced team;
- every slot's `RequiredCapabilities` is satisfiable by at least one `AgentDefinitionBlueprint`
  claiming that role, **declared inline in this blueprint pack** (no alias to resolve);
- every `HumanTask` step's `Role` is declared in `Roles` and its capabilities in
  `Capabilities`;
- every `AgentDefinitionBlueprint`'s `RequiredProviderCapability` and
  `RequiredToolCapabilities` are well-formed capability tags — not aliases, so there is
  nothing tenant-local left to resolve at this point.

What publish does **not** validate is whether any installing tenant currently has a provider
or tool binding that satisfies those capability tags. That is **readiness**, checked
separately, per tenant, at activation — not at publish, and not by rejecting the publish.
This replaces the earlier plan (alias resolution against the *publishing* tenant's registry)
which only proved the class worked for whichever tenant happened to publish it, and is the
fix for gap 8. A `requires` manifest — the capability tags named by every
`AgentDefinitionBlueprint` and every narrowing step, deduplicated — is computed at publish
time, stored alongside the class, and is what a tenant's readiness check walks.

This closure applies to **both** paths. `WorkflowClassBlueprint` gains the same alias
validation for its own `AgentProvider`/`AgentPrompt`/`AgentTools` references (those remain
tenant-scoped aliases on that path — see §9), which fixes gap 1 for the human path that is
live today.

### 3. Effective configuration is computable, not merged in code

The agent definition is authoritative. A step may **narrow** — fewer required tool
capabilities, a lower autonomy ceiling — and may never substitute a different prompt or
require a different provider than the definition declares. Narrowing is expressible in the
declaration; substitution is not.

Consequently the effective agent for any step is derivable by reading declarations, which is
what makes the chain *declarative* rather than merely configured. `AgentProvider` and
`AgentPrompt` on `AgentStepBlueprint` are therefore **not** added; `AgentTools` is retained
as a narrowing filter over the definition's `RequiredToolCapabilities`.

### 4. Autonomy composes downward only

```
effective = min(tenant setting, AgentDefinitionBlueprint ceiling, team ceiling, step override)
```

A later link may restrict; it may never widen. `AgentTeam` already documents
override-downward-only, but with four levels the rule must be stated or a union will be
implemented by accident. The value of a step's override is validated against the enum as of
the preceding change; the *direction* becomes checkable once a step's role resolves to a
team and an agent definition.

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

### 9. The agent definition is application-scoped; its LLM and tool bindings are tenant-scoped *(added by amendment)*

**What an agent *is* travels with the blueprint pack. What an agent *runs on* belongs to the
tenant that runs it.**

Split `AgentProfileConfiguration`'s fields along that line, because they were never one
concept:

| Field (today, tenant-scoped) | Scope after the split | Why |
|---|---|---|
| `Role` | **Application** — `AgentDefinitionBlueprint.Role` | Names a role within the pack's own vocabulary; identical meaning wherever the pack is installed. |
| `PromptAlias` → prompt content | **Application** — `AgentDefinitionBlueprint.Prompt` | The instructions are the agent's behaviour, not tenant infrastructure. Declared inline (or as a content-addressed reference within the pack), not as an alias into a mutable tenant table. |
| `AutoCommitThreshold` | **Application** — `AgentDefinitionBlueprint.MaxAutonomyLevel` | A ceiling the blueprint author sets; composes downward per §4 regardless of which tenant runs it. |
| `AllowedEvents` | **Application** — `AgentDefinitionBlueprint.AllowedEvents` | Part of the agent's declared behaviour envelope. |
| *(new)* required tool capability tags | **Application** — `AgentDefinitionBlueprint.RequiredToolCapabilities` | A capability the agent needs (e.g. "can search a ticket system"), not which tenant connector supplies it. Replaces naming `ToolAliases` directly. |
| *(new)* required provider capability tag | **Application** — `AgentDefinitionBlueprint.RequiredProviderCapability` | What the agent needs from an LLM (e.g. "128k context, function calling"), not which tenant's provider account supplies it. |
| `ProviderAlias` → concrete LLM + model + credentials | **Tenant** — plugin binding registry, `agent`/`decision` | Genuinely a tenant's own account, keys, and rate limits. Unavoidably local. |
| `ToolAliases` → concrete connector + credentials | **Tenant** — plugin binding registry, `action` | Genuinely a tenant's own integration and credentials. Unavoidably local. |

Nothing here removes the plugin binding registry or `AgentVersion`; it corrects what each
one is allowed to hold. `AgentVersion` stops being described as "the published agent" in the
singular — it becomes the **compiled join** of an `AgentDefinitionBlueprint` (one, shared,
application-scoped) and a tenant's provider/tool resolution (one per tenant), produced by
activation exactly the way `AgentTeamBlueprint → compiler → AgentTeam` and
`WorkflowClassBlueprint → WorkflowClassCompiler → WorkflowStepDefinition` already work. Two
tenants installing the same published class get two `AgentVersion` rows that necessarily
agree on role, prompt, and autonomy ceiling — because those came from the one immutable
definition — and may legitimately disagree on which model or which ticket-system connector
backs them.

This is also the resolution to the binding-portability problem raised separately (an earlier
proposal to reject publish on unresolved aliases conflated structural validity with tenant
readiness): with the agent definition inline and tenant bindings looked up by capability
tag rather than by alias, publish validates structure only (§2); a tenant's *readiness* to
run the class — does it hold a provider/tool binding for every tag in the `requires`
manifest — is a separate, re-checkable, per-tenant question, and an unready tenant gets an
explicit `enabled: false` placeholder it must complete before activation, never a silent
alias failure at dispatch and never a blocked publish for every other tenant.

Consequences of the split, stated plainly:

- A `Public`-scope class is now actually portable: install it in a new tenant, and every
  step's role, prompt, and autonomy ceiling are already correct, unconditionally. Only
  provider and tool bindings need setting up — exactly the part that is genuinely
  tenant-specific.
- `AgentProfileConfiguration` as it exists today is not deleted; it is read as the
  **tenant resolution** record (`ProviderAlias`, `ToolAliases`) and its `Role`/`PromptAlias`/
  `AutoCommitThreshold`/`AllowedEvents` fields are superseded by the matching
  `AgentDefinitionBlueprint` fields. A migration note, not a code change, belongs in the
  implementing PR: existing tenant profiles were acting as the only copy of the agent
  definition, so backfilling `AgentDefinitionBlueprint` from them (rather than discarding
  the data) is required wherever a published `AgentWorkflowClass` already references one.
- This does not change `WorkflowClassBlueprint` (the human path). Its `AgentProvider` /
  `AgentPrompt` / `AgentTools` aliases stay tenant-scoped, because that path has no
  application-scoped pack to hold a portable definition — ADR-006/007's declarative chain
  is specific to `AgentWorkflowBlueprint`.

---

## Architecture

### New records

```
AgentDefinitionBlueprint                     (new — added by amendment; application-scoped)
  ├── Alias: string                          unique within the pack; what AgentTeamSlotBlueprint
  │                                          .DefaultDefinitionAlias and SatisfiesCapabilities
  │                                          reference — no tenant alias involved
  ├── Role: string                           was AgentProfileConfiguration.Role
  ├── Prompt: string                         was .PromptAlias → content; declared inline
  ├── MaxAutonomyLevel: AgentAutonomyLevel   was .AutoCommitThreshold; a ceiling, composes per §4
  ├── AllowedEvents: string[]                was .AllowedEvents
  ├── RequiredToolCapabilities: string[]     capability tags, not .ToolAliases
  └── RequiredProviderCapability: string?    capability tag, not .ProviderAlias

AgentTeamBlueprint                          (new blueprint, versioned + publish-immutable)
  ├── Name, Objective
  ├── MaxAutonomyLevel: AgentAutonomyLevel   team ceiling
  └── Slots: AgentTeamSlotBlueprint[]
        ├── RoleName: string                 declares the role vocabulary
        ├── RequiredCapabilities: string[]
        └── DefaultDefinitionAlias: string?  an AgentDefinitionBlueprint.Alias in this pack
                                              (was DefaultProfileAlias — a tenant alias)

AgentWorkflowBlueprint                       (existing — additions)
  ├── Agents: AgentDefinitionBlueprint[]     application-scoped agent definitions, inline
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
        ├── RequiredCapabilities: string[]   narrowing only, over the definition's tags
        └── AgentTools: string[]             narrowing filter over the definition's
                                              RequiredToolCapabilities

AssignmentConstraint                         (new)
  ├── Rule: "distinct" | "notSubjectOf" | "seniorTo"
  ├── Between: string[]                      "Marker#0", "Marker#*", "moderate.assignee"
  └── Subject: string?                       context expression
```

`TeamRole` is retained as a deprecated alias for one release so existing Phase 4A drafts
keep validating, and is rejected on publish once `AgentRole` is available. Likewise
`DefaultProfileAlias` is retained, deprecated, as an escape hatch for a slot that must
resolve directly to a tenant-scoped `AgentProfileConfiguration` (no portable definition
declared yet); `DefaultDefinitionAlias` is the path every new blueprint should use.

Constraints are validated structurally at publish — do the named roles and steps exist — and
evaluated at assignment time, the only moment actual holders are known.

### Error code prefixes

`AWF-` (agent workflow) is established. `ATM-` for `AgentTeamBlueprint`, `EAC-` for the
external agent contract, `ADF-` *(added by amendment)* for `AgentDefinitionBlueprint`
structural errors (unknown capability tag format, duplicate alias within a pack). Alias-
resolution failures take `AWF-REF-*` and, on the human path, `WF-REF-*`. A tenant's
readiness gap against the `requires` manifest takes `AWF-READY-*` — a readiness failure,
never a publish failure (§2, §9).

---

## What does NOT change

- `WorkflowClassBlueprint` semantics — it gains alias validation only.
- `WorkflowEngine` — untouched. Agent plans still do not dispatch; that is Phase 4B.
- `Team` (human IAM) — ADR-002's separation from `AgentTeam` is unaffected.
- The plugin binding registry itself — it still holds every tenant's concrete LLM and tool
  connector bindings. *(Narrowed by amendment: it no longer also holds the portable half of
  the agent definition — see §9. The original bullet asserting `AgentProfileConfiguration`
  was "already right and not re-declared" is withdrawn.)*
- The MCP tool surface — no new tools; guidance updates with each behaviour.

---

## Successor ADRs

This ADR closes the declaration chain. Nothing else proceeds until it does, because every
successor references roles, capabilities or profiles that must first resolve.

| Cluster | Content | Depends on |
|---|---|---|
| **ADR-007** *(this)* | Chain closure: role vocabulary, team blueprint, reference validation, human gate, duty separation, autonomy composition, **application-scoped agent definition vs. tenant-scoped LLM/tool binding (amendment)** | — |
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
- Four blueprints will exist, now five with `AgentDefinitionBlueprint`. Their validators
  must share structure or they will drift.
- Alias validation will reject blueprints that currently publish. Intended, but it means the
  human-path change needs a migration pass over stored classes to find references that no
  longer resolve.
- *(Added by amendment.)* A published `AgentWorkflowClass` is portable across tenants for
  the first time: role, prompt, and autonomy ceiling no longer depend on what the installing
  tenant happens to have aliased. This is the direct fix for the binding-portability problem
  and makes `Public`-scope agent blueprints meaningful rather than aspirational.
- *(Added by amendment.)* Existing published `AgentWorkflowClass` rows that reference a
  tenant's `AgentProfileConfiguration` directly need a backfill pass to synthesize an
  `AgentDefinitionBlueprint` from that profile's `Role`/`PromptAlias`/`AutoCommitThreshold`/
  `AllowedEvents`, so the class keeps validating under the new, stricter structural check
  rather than being silently grandfathered around it.
- *(Added by amendment.)* `AgentVersion.TenantId` stops meaning "the tenant that owns this
  agent" and starts meaning "the tenant that resolved this agent" — a one-word change in
  meaning that every reader of that field, including existing telemetry and audit queries,
  needs to absorb.

### Open decision for review

`AgentTeam` carries its own `AgentTeamStatus` (`Draft` / `Active` / `Archived`) with
`Activate()` and `Archive()`. If `AgentTeamBlueprint` owns a `Draft` → `Published` →
`Deprecated` / `Abandoned` lifecycle, one concept has two lifecycles and they will disagree.

Recommendation: the blueprint's lifecycle is authoritative and the entity's status becomes
derived, with `Activate` / `Archive` becoming compiler-internal. The alternative is correct
only if a published team should be withdrawable from service without deprecating its
declaration. This must be settled before `AgentTeamBlueprint` is implemented.
