# Event Registry & Configuration Guide

## 1. Core Concept
FlowOS has moved from loose, string-based events (e.g., "Approved") to a **Centralized Event Registry**. This ensures that all events driving Workflows and State Machines are:
1.  **Explicitly Defined**: No "magic strings" in code.
2.  **Validated**: The engine rejects unknown events at runtime.
3.  **Reusable**: A single `EventId` can drive multiple engines.

### The "Single Event, Dual Consumption" Principle
A single registered event (e.g., `EVT-ORDER-APPROVED`) acts as a shared fact that:
*   **State Machine**: Validates if the transition is *legal* (e.g., `Pending` -> `Approved`).
*   **Workflow**: Triggers the *next step* (e.g., Move to "Finance Review" step).

---

## 2. Naming Conventions
Event IDs must be stable, unique, and human-readable.

**Format**: `EVT-{ENTITY}-{ACTION}`
*   **Prefix**: Always `EVT-`
*   **Entity**: The domain entity (e.g., `ORDER`, `USER`, `DOCUMENT`)
*   **Action**: What happened (e.g., `APPROVED`, `CREATED`, `REJECTED`)

**Examples**:
*   `EVT-ORDER-SUBMITTED`
*   `EVT-USER-REGISTERED`
*   `EVT-DOCUMENT-SIGNED`

---

## 3. How to Register Events
Currently, events are registered via data seeding (or direct DB insertion).

### Using DataSeeder (Development)
Add new definitions in `DataSeeder.cs`:

```csharp
var evt = new EventDefinition(
    "EVT-ORDER-APPROVED",           // EventId (Immutable Key)
    tenantId,                       // Tenant Scope
    "Order Approved",               // Display Name
    "Triggered when manager approves", // Description
    "Order",                        // Entity Type
    EventCategory.Decision          // Category: Decision | System | Human | Agent
);
context.EventDefinitions.Add(evt);
```

---

## 4. Configuration Guide

### A. State Machine Configuration
Use the `eventId` field in your transitions.

**❌ Old (Deprecated)**
```json
{
  "from": "Pending",
  "triggerEventType": "Approved",
  "to": "Approved"
}
```

**✅ New (Recommended)**
```json
{
  "from": "Pending",
  "eventId": "EVT-ORDER-APPROVED",
  "to": "Approved"
}
```

### B. Workflow Configuration
Use the `EventId` as the key in the `NextSteps` dictionary.

**❌ Old (Deprecated)**
```csharp
new WorkflowStepDefinition("Review") {
    NextSteps = { { "Approved", "FinanceStep" } }
}
```

**✅ New (Recommended)**
```csharp
new WorkflowStepDefinition("Review") {
    NextSteps = { { "EVT-ORDER-APPROVED", "FinanceStep" } }
}
```

---

## 5. Runtime Behavior & Validation

1.  **Publishing Events**:
    When calling the API:
    ```http
    POST /api/events/publish
    {
      "eventType": "EVT-ORDER-APPROVED",
      ...
    }
    ```

2.  **Validation Logic**:
    *   **Strict Check**: If the event string starts with `EVT-`, the system **verifies it exists** in the Registry. If missing, the request is rejected (400 Bad Request).
    *   **Legacy Support**: If the string does *not* start with `EVT-` (e.g., "Approved"), it is currently allowed but logs a warning (Phase 1).

---

## 6. Migration Checklist (Phase 1 -> Phase 2)
To migrate existing workflows:

1.  [ ] **Inventory**: List all "magic string" events currently in use.
2.  [ ] **Register**: Create `EventDefinition` entries for each string (e.g., "Approved" -> `EVT-ORDER-APPROVED`).
3.  [ ] **Update Config**: Update Workflow and State Machine definitions to use the new IDs.
4.  [ ] **Test**: Verify flow works with new IDs.
5.  [ ] **Deprecate**: Once all configs are updated, disable support for non-`EVT-` strings in `WorkflowCommandHandlers`.
