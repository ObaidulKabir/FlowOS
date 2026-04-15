# FlowOS Kernel Specification & Developer Manual

**Version 1.1.0**
*Enterprise Process Operating System*

FlowOS is a kernel-style process engine designed for correctness, compliance, and enterprise scale. Unlike traditional workflow engines, FlowOS strictly separates **State Authority (State Machines)** from **Process Orchestration (Workflows)** and **Business Logic (Policy & Agents)**.

This document serves as the authoritative specification for developing on FlowOS.

---

## 1. Core Philosophy & Invariants

FlowOS is built on a set of non-negotiable invariants. These rules guarantee system integrity.

| Component         | Responsibility                | Authority    | Constraints                              |
|:----------------- |:----------------------------- |:------------ |:---------------------------------------- |
| **State Machine** | Enforce entity state legality | **Absolute** | Cannot be bypassed by Workflow or Admin. |
| **Workflow**      | Orchestrate time & steps      | High         | Must obey State Machine rules.           |
| **Event Log**     | Record history                | **Absolute** | Immutable. Append-only.                  |
| **Policy**        | Govern commands               | Gatekeeper   | **Deny-only**. Cannot mutate state.      |
| **AI Agents**     | Analyze & Suggest             | None         | **Advisory-only**. Read-only access.     |
| **Admin UI**      | Observe & Audit               | None         | **Read-only**. No "force" operations.    |

---

## 2. 🔐 State Machines (The Law)

FlowOS enforces all entity state changes through **State Machines**.

* **Workflows orchestrate time** (when to do something).
* **State Machines enforce legality** (if it can be done).

A workflow **can never** bypass a state machine. If a workflow attempts an illegal transition, the State Machine Engine rejects it, and the workflow halts or errors.

### Definition Structure (JSON Configuration)

**Definition:** A State Machine defines the legal states and transitions for a business entity (e.g., "Order").
**Principle:** It serves as the "System of Record". No workflow can move an entity to a state not defined here.

```json
{
  "entityType": "Order",
  "initialState": "Created",
  "states": [ "Created", "PendingApproval", "Approved", "Rejected" ],
  "transitions": [
    {
      "fromState": "Created",
      "toState": "PendingApproval",
      "triggerEventType": "Submit"
    },
    {
      "fromState": "PendingApproval",
      "toState": "Approved",
      "triggerEventType": "Approve"
    },
    {
      "fromState": "PendingApproval",
      "toState": "Rejected",
      "triggerEventType": "Reject"
    }
  ]
}
```

### Bootstrap (C#)

```csharp
var orderStateMachine = new StateMachineDefinition(
    entityType: "Order",
    initialState: "Created"
);
// ...
```

### Invariant

> **Rule:** No entity can exist in a state undefined by its State Machine, and no entity can move between states without a valid transition trigger.

---

## 3. 📜 Events (The Truth)

Events are the atomic unit of truth in FlowOS.

* **Immutable**: Once written, never changed or deleted.
* **Derived State**: All current state (Workflow Status, Entity State) is a projection of the Event Log.
* **Correlation**: All events carry a `CorrelationId` (usually the `WorkflowInstanceId`) and `TenantId`.

### Event Types

1. **Command Events**: Intent to change state (e.g., `WorkflowStarted`).
2. **Fact Events**: Something happened (e.g., `TaskCompleted`, `AgentInsightGenerated`).
3. **State Events**: The system changed (e.g., `StateTransitioned`).

### Event Payload Example (JSON)

**Definition:** An event is an immutable record of something that happened in the system.
**Principle:** Events are the "Source of Truth". System state is derived by replaying these events.

```json
{
  "eventId": "a1b2c3d4-...",
  "tenantId": "...",
  "timestamp": "2023-10-27T10:00:00Z",
  "eventType": "TaskCompleted",
  "correlationId": "wf-instance-123",
  "version": 1,
  "metadata": {
    "userId": "user-456",
    "ipAddress": "192.168.1.1"
  }
}
```

### Invariant

> **Rule:** If it's not in the Event Log, it didn't happen. Replaying the Event Log must deterministically reconstruct the system state.

---

## 4. 📦 Versioning & Immutability

FlowOS solves "in-flight process" problems via strict versioning.

### Rules

1. **Definitions are Immutable**: Once a `WorkflowDefinition` (v1) is published, it is **frozen**.
2. **Instance Pinning**: A `WorkflowInstance` started on v1 **stays** on v1 forever.
3. **New Versions**: Deploying v2 only affects **new** instances.

### Handling Change

To "migrate" a running process, you must explicitly terminate the v1 instance and start a new v2 instance (if business rules allow). FlowOS does not support "hot-swapping" logic on live instances, as this breaks auditability.

---

## 5. �️ Policy & Governance

Policies are **Deny-Only** interceptors that run before the Engine.

### Capabilities

* ✅ Check User Roles
* ✅ Check Time/Date
* ✅ Check Business Constraints
* ✅ Return `Allowed` or `Denied`

### Policy Configuration (JSON)

**Definition:** A Policy is a rule that governs whether a command is allowed to execute.
**Principle:** Policies are "Deny-Only". They can prevent an action but cannot perform one.

```json
{
  "policyId": "WeekendFreeze",
  "name": "Weekend Operations Freeze",
  "description": "Prevents approvals on weekends",
  "evaluatorType": "FlowOS.Policies.WeekendFreezePolicy",
  "configuration": {
    "frozenDays": ["Saturday", "Sunday"]
  }
}
```

### Strict Prohibitions

* ❌ **Cannot** Mutate State (No DB writes)
* ❌ **Cannot** Emit Events
* ❌ **Cannot** Advance Workflows
* ❌ **Cannot** Call External Engines

```csharp
public class WeekendFreezePolicy : IPolicyEvaluator
{
    public Task<PolicyResult> EvaluateAsync(PolicyContext context)
    {
        // Pure function: Context -> Result
        if (DateTime.UtcNow.DayOfWeek == DayOfWeek.Saturday)
            return Task.FromResult(PolicyResult.Deny("Weekend freeze."));

        return Task.FromResult(PolicyResult.Allow());
    }
}
```

---

## 6. 🤖 AI Agents (Advisory)

FlowOS supports AI Agents under strict containment rules ("MCP-style").

### Agent Contract

1. **Input**: `AgentContext` (Read-only snapshot of Tenant, Entity, Workflow State).
2. **Output**: `AgentResult` (Insight string, Structured Data, **Suggested Actions**).
3. **Side Effects**: **None**.

### Insight Projection & Smart Actions

Agents emit `AgentInsightGenerated` events. These are recorded in history but **do not** trigger state transitions directly.

* **Insights**: Descriptive analysis (e.g., "High Risk").

* **Suggested Actions**: Actionable proposals (e.g., "Suggest Escalate").
  
  * These appear in the UI as **Smart Actions** buttons.
  * A human user must click "Confirm" to execute the suggested event.

* **Correct**: "Agent suggests Approval (90%)". Human/Rule reads this and acts.

* **Incorrect**: Agent calls `Approve()`. (Impossible in FlowOS).

---

## 7. 👤 Human Tasks & API

The Human Task interface is a **Read/Write separation**.

### Reading (Query API)

The UI reads from a **Projection**, not the Engine.

* `GET /api/tasks/{id}` returns the task + projected Agent Insights.

### Writing (Command API)

The UI emits intent, it does not change state.

* `POST /api/tasks/{id}/complete` -> Emits `TaskCompleted`.
* The **Engine** consumes `TaskCompleted`.
* **IF** the Workflow Definition has a transition for `TaskCompleted` at the current step, **THEN** it advances.
* Otherwise, the event is recorded, but state does not change.

---

## 8. � Failure & Recovery

FlowOS is designed for crash resilience.

* **Idempotency**: Processing the same message twice produces the same result (deduplicated by Message ID).
* **Atomic Transactions**: DB Writes and Event Emissions happen atomically.
* **Resume**: On process restart, the Engine re-loads state from the DB. Since state is event-derived, no "in-memory" progress is lost.

---

## 9. 🏢 Multi-Tenancy

FlowOS is a multi-tenant kernel.

* **Isolation**: Every data row (Event, Workflow, Definition) is keyed by `TenantId`.
* **Scope**: Policies and Definitions are scoped to a Tenant.
* **Security**: `ICurrentUser` must resolve `TenantId` securely (Header/Token) before any Command is processed.

---

## 10. 👁️ Admin & Visibility

The Admin API provides deep observability but **zero mutability**.

### What Admins Can See

* Full Event Timeline (Curated for readability).
* Current State & Step.
* Definition Versions.
* Agent Insights.

### What Admins Cannot Do

* "Fix" a workflow state manually (Must emit a compensating event).
* "Force" a transition (Must emit a trigger event).
* "Delete" history (Impossible).

---

## 11. Agent Functionality & Constraints (Design-Time Governance)

### 11.1 Purpose

This section defines the constitutional role, authority limits, and behavioral constraints of Agents operating within FlowOS via MCP.

* **Agents are reasoning participants, not system actors.**
* **Their outputs are proposals, not actions.**

### 11.2 Definition of an Agent

An Agent is a non-human reasoning entity (AI or automated system) that:

* Analyzes problem statements
* Proposes design-time artifacts
* Interacts exclusively through MCP design-time tools
* Operates under strict governance and auditability rules

**Agents do not execute, do not decide, and do not commit.**

### 11.3 Scope of Agent Authority (Strict)

**Agents MAY:**

* Propose new WorkflowClass Drafts
* Modify existing Draft blueprints
* Request authoritative validation
* Interpret and explain validation violations
* Iterate designs based on feedback
* Propose notification mappings and policies (design-time)

**Agents MAY NOT:**

* Execute workflows or steps
* Publish WorkflowClasses
* Advance WorkflowInstances
* Emit Domain Events
* Modify runtime data
* Bypass validation or governance
* Access tenant operational data

**Any attempt to exceed this scope is INVALID.**

### 11.4 MCP as the Sole Interaction Surface

Agents MUST interact with FlowOS only through MCP-exposed tools.
This implies:

* No direct API access
* No database access
* No runtime hooks
* No hidden capabilities

If an operation is not exposed via MCP, it is out of bounds for Agents.

### 11.5 Proposal-Only Principle

All Agent outputs are non-authoritative proposals. This includes:

* WorkflowClass designs
* StateMachine definitions
* Workflow structures
* Event vocabularies
* Policy suggestions
* Notification mappings

A proposal:

* Has no effect until validated
* Has no effect until published by an authorized actor
* May be rejected without partial acceptance

### 11.6 Validation Subjection Rule

Agents are fully subject to FlowOS validation.

* All proposals MUST pass `WorkflowClassValidator`
* All violations MUST be surfaced to the Agent
* Agents MUST respond to violations explicitly
* Silent correction is **FORBIDDEN**

Agents may explain errors, but may not override them.

### 11.7 No Authority Inference Rule

Agents MUST NOT infer authority from:

* Successful validation
* Prior approvals
* Repeated acceptance
* Contextual hints
* External instructions

**Validation success does not imply permission to act.**

### 11.8 Auditability of Agent Output

All Agent involvement MUST be auditable.

* Agent proposals to be traceable to input context
* Validation results to be recorded
* Rejections to cite explicit RuleIds
* Accepted designs to preserve proposal lineage

Agents are never a "black box".

### 11.9 Determinism & Reproducibility

Given the same inputs, rules, and validation logic, an Agent’s reasoning process SHOULD be reproducible to a reasonable degree. Non-determinism MUST NOT affect system correctness.

### 11.10 Prohibited Agent Anti-Patterns

The following are **STRICTLY FORBIDDEN**:

* **AG-001 — Acting as Executor**: Attempting to perform runtime actions.
* **AG-002 — Validation Circumvention**: Ignoring or downplaying validation failures.
* **AG-003 — Implicit Authority Claims**: Assuming permission without explicit grant.
* **AG-004 — Silent Auto-Fix**: Modifying designs without explaining violations.
* **AG-005 — Runtime Reasoning**: Making assumptions based on live system state.

### 11.11 Relationship to Roles, Capabilities & Policies

Agents:

* Are not Roles
* Do not possess Capabilities
* Are subject to Policies when applicable

Agents may reason about governance but are never governed as actors.

### 11.12 Failure & Uncertainty Handling

If an Agent is uncertain about Rule interpretation, Structural correctness, or Semantic meaning of Events, the Agent MUST:

* Declare the uncertainty
* Treat the proposal as INVALID
* Request clarification or propose a conservative alternative

**Guessing is FORBIDDEN.**

### 11.13 Supremacy Clause

If any Agent behavior conflicts with StateMachine law, Workflow structure, Validation rules, Auditability guarantees, or Governance constraints, the Agent yields immediately.

**FlowOS remains the sole authority.**

---

## 12. Quick Start (Configuration)

### Definition Structure (JSON Configuration)

**Definition:** A Workflow Definition describes the sequence of steps and time-based orchestration.
**Principle:** Workflows handle "When" things happen. They must align with the State Machine ("What" is legal).

```json
{
  "name": "ExpenseApproval",
  "version": 1,
  "steps": [
    {
      "stepId": "Submit",
      "stepType": "Command",
      "nextSteps": {
        "Submitted": "ManagerReview"
      }
    },
    {
      "stepId": "ManagerReview",
      "stepType": "HumanTask",
      "allowedRoles": [ "Manager" ],
      "nextSteps": {
        "Approved": "FinanceReview",
        "Rejected": "End"
      }
    },
    {
      "stepId": "FinanceReview",
      "stepType": "HumanTask",
      "allowedRoles": [ "Finance" ],
      "nextSteps": {
        "Paid": "End"
      }
    }
  ]
}
```

### Bootstrap (C#)

If you are bootstrapping via code (e.g., in tests or seeders):

```csharp
var definition = new WorkflowDefinition(tenantId, "ExpenseApproval", 1);

// Step 1: Submit (Command)
definition.AddStep(new WorkflowStepDefinition("Submit", WorkflowStepType.Command)
{
    NextSteps = { { "Submitted", "ManagerReview" } }
});
// ...
definition.Publish();
```

### Executing

```bash
# Start
POST /api/workflows/start
{ "definitionId": "...", "version": 1 }

# Complete Task (Emits 'TaskCompleted' event)
POST /api/tasks/{id}/complete
```

---

## 12. 🔔 Notifications (User Awareness)

Notifications are an **Out-of-Band** projection of the Event Log.

### Philosophy

* **State is Authoritative**: The Workflow Engine (Database) is the single source of truth.
* **Notifications are Informational**: They are a derived view designed for human attention.
* **Isolation**: A failure to deliver a notification (e.g., email server down) **MUST NOT** rollback the core business transaction.

### Mechanism

1. **Commit**: Transaction commits to DB (Event Log + State).
2. **Project**: An asynchronous (or post-commit) hook projects the Event into a `Notification` record.
3. **Broadcast**: The Notification is pushed to real-time channels (SSE, Websockets).

### Guarantees

* **At-Least-Once**: You might receive duplicate notifications for the same event (idempotency side-effect).
* **Eventual Consistency**: Notification arrives ms/seconds after the state change.

---

## 14.  WorkflowClass Governance

FlowOS introduces `WorkflowClass` as the strict unit of authoring and governance.

### Core Concept

A `WorkflowClass` is a versioned "Configuration Pack" that bundles:

* **Vocabulary** (Events)
* **Law** (State Machine)
* **Orchestration** (Workflow)
* **Governance** (Roles & Capabilities)

### Lifecycle & Scope

* **Scopes**: `Private` (Tenant-only), `Shared` (Review), `Public` (Template).
* **Status**: `Draft` -> `Published` -> `Shared` -> `Public`.
* **Copy-to-Use**: Public templates are immutable. Tenants must **Copy** them (resetting version to v1) to use them.

### Validation

Server-side validation is mandatory and authoritative. It checks:

* **Structure**: Schema validity.
* **Consistency**: All references resolve (e.g., `CON-004` checks that transitions point to valid steps).
* **Law**: Workflow respects State Machine.
* **Governance**: Roles/Capabilities declared.

**Important:** Validation runs during **Draft Creation** (to prevent broken graphs) and again at **Publish** time (for strict compliance).

See [11 - WorkflowClass Authoring](sdk/11-workflow-class-authoring.md) for details.
