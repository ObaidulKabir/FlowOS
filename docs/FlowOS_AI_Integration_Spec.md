# FlowOS AI Integration Specification & User Manual

This document serves as the authoritative specification for AI models and external systems interacting with FlowOS. It defines the mental model, data structures, validation rules, and operational guidelines required to validly create, manage, and execute workflows within the system.

## 1. System Overview

FlowOS is a governance-first workflow engine that decouples **Business Law** (State Machine) from **Operational Work** (Workflow Orchestration).

*   **Mental Model**:
    *   **WorkflowClass**: The immutable "Class" or template definition (e.g., "ExpenseApproval v1.0").
    *   **WorkflowInstance**: The running "Object" or execution context (e.g., "Expense #123").
    *   **Blueprints**: The JSON configuration structures used to define a WorkflowClass.

## 2. Core Type Definitions

AI models generating FlowOS configurations must adhere to these JSON structures.

### 2.1. WorkflowClassBlueprint
The root container for a process definition.

```json
{
  "events": [ /* List of EventBlueprint */ ],
  "stateMachine": { /* StateMachineBlueprint */ },
  "workflow": { /* WorkflowBlueprint */ },
  "roles": [ /* List of RoleBlueprint */ ],
  "capabilities": [ /* List of CapabilityBlueprint */ ]
}
```

### 2.2. EventBlueprint (Vocabulary)
Defines the signals that drive the process.

```json
{
  "eventId": "String (Unique, e.g., 'EVT-SUBMIT')",
  "name": "String (Human Readable)",
  "category": "Enum (System | Human | Decision | Agent)",
  "isTerminal": "Boolean (True if this event ends the lifecycle)",
  "payloadSchema": "String (Optional JSON Schema object for validation)"
}
```

### 2.3. StateMachineBlueprint (The Law)
Defines the valid states and allowed transitions.

```json
{
  "initialState": "String (Must match one of the States)",
  "states": [ "String" ],
  "transitions": [
    {
      "fromState": "String",
      "toState": "String",
      "eventId": "String (Must be defined in Events)"
    }
  ]
}
```

### 2.4. WorkflowBlueprint (The Work)
Defines the sequence of steps.

```json
{
  "startStepId": "String (ID of the first step)",
  "steps": [ /* List of StepBlueprint */ ]
}
```

### 2.5. StepBlueprint
A single unit of work.

```json
{
  "stepId": "String (Unique within workflow)",
  "stepType": "Enum (Command | HumanTask | SystemTask | Decision | Timer | End)",
  "nextSteps": {
    "EventId_or_Default": "NextStepId"
  },
  "requiredRoles": [ "String (Role Name)" ],
  "conditions": {
    "ConditionExpression": "NextStepId" // Only for Decision steps
  }
}
```

---

## 3. Operational Rules & Validation

FlowOS enforces strict validation. AI models must ensure generated blueprints comply with these invariants.

### 3.1. Structural Integrity
*   **Mandatory Fields**: `InitialState`, `StartStepId`, and at least one Step are required.
*   **Reference Integrity**:
    *   `StartStepId` must exist in the `Steps` list.
    *   All `NextSteps` values must point to existing Step IDs or the reserved keyword `"END"`.
    *   All `fromState` and `toState` values must exist in the `States` list.
    *   All `eventId` references must exist in the `Events` list.

### 3.2. Step Type Constraints
*   **Command / SystemTask**: Must have at least one exit path (usually `Default` or a specific Event).
*   **HumanTask**: Must define `NextSteps` triggered by Human Events (e.g., `EVT-APPROVE`).
*   **Decision**:
    *   **MUST** have a `Conditions` map.
    *   **MUST NOT** rely on `NextSteps` for routing (routing is determined by condition evaluation).
*   **End**: **MUST NOT** have any `NextSteps`.

### 3.3. Data Validation
*   **PayloadSchema**: If provided, it must be a valid JSON Schema object.
    *   *Example*: `"{ \"type\": \"object\", \"properties\": { \"amount\": { \"type\": \"number\" } } }"`

---

## 4. Guidelines for Workflow Creation

### 4.1. Designing for Governance
*   **Separation of Concerns**: Use the State Machine to enforce business rules (e.g., "An Expense cannot go from Draft to Paid without Approval"). Use the Workflow to define *how* that approval happens (e.g., "Manager reviews, then Director reviews").
*   **Role Least Privilege**: Define granular roles (e.g., `RiskOfficer`) rather than generic ones (`Admin`). Assign specific capabilities to roles.

### 4.2. Handling Automation
*   **System Tasks**: Use `SystemTask` for backend operations (e.g., API calls, DB updates). These steps should auto-advance using a `Default` transition or a System Event.
*   **Decisions**: Use `Decision` steps for logic branching.
    *   *Pattern*: `CheckFraud` (SystemTask) -> emits `Payload.FraudScore` -> `FraudRouting` (Decision) checks `Payload.FraudScore > 50`.

### 4.3. AI Integration
*   **Advisory Role**: AI Agents participate by emitting `EVT-AGENT-INSIGHT`. They do not directly change state unless authorized as a System Agent.
*   **Human-in-the-loop**: For high-stakes decisions, design a `HumanTask` that allows a user to review AI Insights before proceeding.

### 4.4. Event Broadcasting
*   FlowOS automatically broadcasts events via SSE. Design workflows to emit meaningful intermediate events (e.g., `EVT-PROCESSING-STARTED`) if the user needs progress updates.

---

## 5. Anti-Patterns (What to Avoid)

*   **Implicit Timers**: Do not assume a task will expire. Use explicit `Timer` steps or external schedulers triggering `EVT-TIMEOUT`.
*   **Orphaned Steps**: Every step must be reachable from the `StartStepId`.
*   **Dead Ends**: Every non-End step must have a valid transition to another step or `END`.
*   **Hidden Logic**: Avoid embedding business rules in code if they can be modeled in the State Machine.

