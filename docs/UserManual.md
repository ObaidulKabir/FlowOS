# FlowOS Kernel Specification & Developer Manual

**Version 1.1.0**
*Enterprise Process Operating System*

FlowOS is a kernel-style process engine designed for correctness, compliance, and enterprise scale. Unlike traditional workflow engines, FlowOS strictly separates **State Authority (State Machines)** from **Process Orchestration (Workflows)** and **Business Logic (Policy & Agents)**.

This document serves as the authoritative specification for developing on FlowOS.

---

## 1. Core Philosophy & Invariants

FlowOS is built on a set of non-negotiable invariants. These rules guarantee system integrity.

| Component | Responsibility | Authority | Constraints |
| :--- | :--- | :--- | :--- |
| **State Machine** | Enforce entity state legality | **Absolute** | Cannot be bypassed by Workflow or Admin. |
| **Workflow** | Orchestrate time & steps | High | Must obey State Machine rules. |
| **Event Log** | Record history | **Absolute** | Immutable. Append-only. |
| **Policy** | Govern commands | Gatekeeper | **Deny-only**. Cannot mutate state. |
| **AI Agents** | Analyze & Suggest | None | **Advisory-only**. Read-only access. |
| **Admin UI** | Observe & Audit | None | **Read-only**. No "force" operations. |

---

## 2. 🔐 State Machines (The Law)

FlowOS enforces all entity state changes through **State Machines**.
*   **Workflows orchestrate time** (when to do something).
*   **State Machines enforce legality** (if it can be done).

A workflow **can never** bypass a state machine. If a workflow attempts an illegal transition, the State Machine Engine rejects it, and the workflow halts or errors.

### Definition Structure
State Machines are defined as data, typically loaded from persistence.

```csharp
var orderStateMachine = new StateMachineDefinition(
    entityType: "Order",
    initialState: "Created"
);

// Define Transitions
orderStateMachine.AddTransition("Created", "Submit", "PendingApproval");
orderStateMachine.AddTransition("PendingApproval", "Approve", "Approved");
orderStateMachine.AddTransition("PendingApproval", "Reject", "Rejected");
// Note: No transition from 'Rejected' to 'Approved'. This is legally impossible.
```

### Invariant
> **Rule:** No entity can exist in a state undefined by its State Machine, and no entity can move between states without a valid transition trigger.

---

## 3. 📜 Events (The Truth)

Events are the atomic unit of truth in FlowOS.
*   **Immutable**: Once written, never changed or deleted.
*   **Derived State**: All current state (Workflow Status, Entity State) is a projection of the Event Log.
*   **Correlation**: All events carry a `CorrelationId` (usually the `WorkflowInstanceId`) and `TenantId`.

### Event Types
1.  **Command Events**: Intent to change state (e.g., `WorkflowStarted`).
2.  **Fact Events**: Something happened (e.g., `TaskCompleted`, `AgentInsightGenerated`).
3.  **State Events**: The system changed (e.g., `StateTransitioned`).

### Invariant
> **Rule:** If it's not in the Event Log, it didn't happen. Replaying the Event Log must deterministically reconstruct the system state.

---

## 4. 📦 Versioning & Immutability

FlowOS solves "in-flight process" problems via strict versioning.

### Rules
1.  **Definitions are Immutable**: Once a `WorkflowDefinition` (v1) is published, it is **frozen**.
2.  **Instance Pinning**: A `WorkflowInstance` started on v1 **stays** on v1 forever.
3.  **New Versions**: Deploying v2 only affects **new** instances.

### Handling Change
To "migrate" a running process, you must explicitly terminate the v1 instance and start a new v2 instance (if business rules allow). FlowOS does not support "hot-swapping" logic on live instances, as this breaks auditability.

---

## 5. �️ Policy & Governance

Policies are **Deny-Only** interceptors that run before the Engine.

### Capabilities
*   ✅ Check User Roles
*   ✅ Check Time/Date
*   ✅ Check Business Constraints
*   ✅ Return `Allowed` or `Denied`

### Strict Prohibitions
*   ❌ **Cannot** Mutate State (No DB writes)
*   ❌ **Cannot** Emit Events
*   ❌ **Cannot** Advance Workflows
*   ❌ **Cannot** Call External Engines

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
1.  **Input**: `AgentContext` (Read-only snapshot of Tenant, Entity, Workflow State).
2.  **Output**: `AgentResult` (Insight string, Structured Data).
3.  **Side Effects**: **None**.

### Insight Projection
Agents emit `AgentInsightGenerated` events. These are recorded in history but **do not** trigger state transitions directly.
*   **Correct**: "Agent suggests Approval (90%)". Human/Rule reads this and acts.
*   **Incorrect**: Agent calls `Approve()`. (Impossible in FlowOS).

---

## 7. 👤 Human Tasks & API

The Human Task interface is a **Read/Write separation**.

### Reading (Query API)
The UI reads from a **Projection**, not the Engine.
*   `GET /api/tasks/{id}` returns the task + projected Agent Insights.

### Writing (Command API)
The UI emits intent, it does not change state.
*   `POST /api/tasks/{id}/complete` -> Emits `TaskCompleted`.
*   The **Engine** consumes `TaskCompleted`.
*   **IF** the Workflow Definition has a transition for `TaskCompleted` at the current step, **THEN** it advances.
*   Otherwise, the event is recorded, but state does not change.

---

## 8. � Failure & Recovery

FlowOS is designed for crash resilience.

*   **Idempotency**: Processing the same message twice produces the same result (deduplicated by Message ID).
*   **Atomic Transactions**: DB Writes and Event Emissions happen atomically.
*   **Resume**: On process restart, the Engine re-loads state from the DB. Since state is event-derived, no "in-memory" progress is lost.

---

## 9. 🏢 Multi-Tenancy

FlowOS is a multi-tenant kernel.

*   **Isolation**: Every data row (Event, Workflow, Definition) is keyed by `TenantId`.
*   **Scope**: Policies and Definitions are scoped to a Tenant.
*   **Security**: `ICurrentUser` must resolve `TenantId` securely (Header/Token) before any Command is processed.

---

## 10. 👁️ Admin & Visibility

The Admin API provides deep observability but **zero mutability**.

### What Admins Can See
*   Full Event Timeline (Curated for readability).
*   Current State & Step.
*   Definition Versions.
*   Agent Insights.

### What Admins Cannot Do
*   "Fix" a workflow state manually (Must emit a compensating event).
*   "Force" a transition (Must emit a trigger event).
*   "Delete" history (Impossible).

---

## 11. Quick Start (Configuration)

### Defining a Workflow (Configuration Data)

While you can use C# to bootstrap, Definitions are fundamentally **data**. FlowOS definitions are typically serialized as JSON.

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
