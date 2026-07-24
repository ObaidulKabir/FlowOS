# 2. Core Concepts

## "Clients React. FlowOS Decides."

Your client application (UI, API consumer, or external service) never decides the next state of a workflow. It only expresses **intent** through commands or events. FlowOS decides what happens as a result.

### The cycle

1. **Intent** — You tell FlowOS what happened (e.g. "Task Completed", "Order Approved") by publishing an event or completing a task.
2. **Decision** — FlowOS evaluates the Workflow Definition, the State Machine rules, and any active Policies.
3. **Transition** — If valid, FlowOS advances the workflow to the next step and/or transitions the entity's state.
4. **Reaction** — You query the new state, or receive a real-time notification (see [Chapter 10](10-notifications.md)).

## The Iron Triangle: Law, Work, Truth

FlowOS strictly separates three concerns. This is the single most important idea in the system — nearly every anti-pattern in [Chapter 12](12-anti-patterns.md) comes from violating this separation.

| Component | Responsibility | Authority | Constraints |
|---|---|---|---|
| **State Machine** (Law) | Enforce entity state legality | **Absolute** | Cannot be bypassed by a Workflow or an Admin. |
| **Workflow** (Work) | Orchestrate steps over time | High | Must obey State Machine rules. |
| **Event Log** (Truth) | Record history | **Absolute** | Immutable, append-only. |
| **Policy** | Govern commands | Gatekeeper | **Deny-only.** Cannot mutate state. |
| **AI Agents** | Analyze & suggest | None | **Advisory-only.** Read-only access. |
| **Admin UI** | Observe & audit | None | **Read-only.** No "force" operations. |

* **Workflows orchestrate time** — they decide *when* to do something.
* **State Machines enforce legality** — they decide *if* it can be done at all.
* A workflow can **never** bypass a state machine. If a workflow attempts an illegal transition, the engine rejects it and the workflow halts (or the event is recorded but ignored — see [Chapter 4](04-events-and-registry.md) for the exact semantics).

## The three primitives, in one sentence each

* **StateMachine = Law.** What is legally true right now, for a given entity type.
* **Workflow = Work.** What work may be performed, and in what order, within the boundaries set by Law.
* **Event = Truth.** An immutable fact that has occurred. Events are the only way to advance either Law or Work.

An Event **may be referenced by both** the StateMachine and the Workflow simultaneously — this is the "single event, dual consumption" principle explained in [Chapter 4](04-events-and-registry.md).

## Versioning & immutability

FlowOS solves the "in-flight process" problem via strict versioning:

1. **Definitions are immutable.** Once a `WorkflowDefinition` (v1) is published, it is frozen.
2. **Instance pinning.** A `WorkflowInstance` started on v1 stays on v1 forever, even after v2 is deployed.
3. **New versions only affect new instances.** To "migrate" a running process, you must explicitly terminate the v1 instance and start a new v2 instance — FlowOS does not support hot-swapping logic on live instances, because that would break auditability.

See [Chapter 5](05-workflows-and-versioning.md) for how this works in practice.

## Failure & recovery

* **Idempotency**: processing the same message twice produces the same result.
* **Atomic transactions**: DB writes and event emissions happen in the same transaction (via `EventPublishingInterceptor`, an EF Core `SaveChangesInterceptor`).
* **Resume**: on process restart, the engine reloads state from the database. Because state is event-derived, no "in-memory" progress is ever lost. See [Chapter 11](11-recovery-and-resilience.md).

## Multi-tenancy

FlowOS is a multi-tenant kernel:

* **Isolation**: every row (`Event`, `WorkflowInstance`, `WorkflowDefinition`, `Policy`, `Role`, ...) is keyed by `TenantId`.
* **Scope**: Policies and Definitions are scoped to a tenant (with an explicit `Public` scope escape hatch for shared `WorkflowClass` templates — [Chapter 9](09-workflow-class-governance.md)).
* **Resolution**: `ICurrentUser.TenantId` resolves the tenant from the `x-tenant-id` header (mock auth) or, in production, from an authentication claim. Every command handler either trusts the resolved tenant or the controller overwrites the caller-supplied `TenantId` with it (see `WorkflowsController.Start`).

## Admin & visibility

The Admin API (`/api/admin/*`) provides deep observability but **zero mutability**:

* **Can**: see the full event timeline, current state/step, definition versions, and agent insights.
* **Cannot**: "fix" a workflow state manually, "force" a transition, or delete history. Every state change must go through a real command (`StartWorkflowCommand`, `PublishEventCommand`, `CompleteTaskCommand`) that is itself subject to policy and state machine enforcement.

## Where to go next

* [Chapter 3 — State Machines](03-state-machines.md) to define legality.
* [Chapter 4 — Events & the Event Registry](04-events-and-registry.md) to define your vocabulary.
* [Chapter 5 — Workflows & Versioning](05-workflows-and-versioning.md) to define orchestration.
