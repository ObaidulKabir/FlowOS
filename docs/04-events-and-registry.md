# 4. Events & the Event Registry (The Truth)

Events are the atomic unit of truth in FlowOS.

* **Immutable** — once written, never changed or deleted.
* **Derived state** — all current state (Workflow status, entity state) is a projection of the Event Log.
* **Correlation** — every event carries a `CorrelationId` (usually the `WorkflowInstanceId`) and a `TenantId`.

> **Rule:** If it's not in the Event Log, it didn't happen. Replaying the Event Log must deterministically reconstruct system state.

## From magic strings to a Centralized Event Registry

FlowOS moved from loose, string-based events (e.g. `"Approved"`) to a **Centralized Event Registry**. This ensures every event driving Workflows and State Machines is:

1. **Explicitly defined** — no magic strings scattered across code and config.
2. **Validated** — the engine rejects unknown `EVT-*` events at runtime.
3. **Reusable** — a single `EventId` can drive both the State Machine and the Workflow ("single event, dual consumption").

## Naming convention

**Format:** `EVT-{ENTITY}-{ACTION}`

* Prefix: always `EVT-`
* Entity: the domain entity (`ORDER`, `USER`, `DOCUMENT`, ...)
* Action: what happened (`APPROVED`, `CREATED`, `REJECTED`, ...)

Examples: `EVT-ORDER-SUBMITTED`, `EVT-USER-REGISTERED`, `EVT-DOCUMENT-SIGNED`.

## Registering an event

### Via configuration (recommended)

Drop a JSON file under `flowos-config/events/`:

```json
{
  "eventId": "EVT-ORDER-APPROVED",
  "displayName": "Order Approved",
  "description": "Triggered when a manager approves an order.",
  "entityType": "Order",
  "category": "Decision"
}
```

`ConfigurationLoader` picks this up on startup or via `POST /api/admin/config/publish`.

### Via code (seeding/tests)

```csharp
var evt = new EventDefinition(
    "EVT-ORDER-APPROVED",              // EventId (immutable key)
    tenantId,                          // Tenant scope
    "Order Approved",                  // Display name
    "Triggered when manager approves", // Description
    "Order",                           // Entity type
    EventCategory.Decision             // System | Human | Decision | Agent
);
evt.Publish(); // Must be explicitly published to be usable
context.EventDefinitions.Add(evt);
```

### Via WorkflowClass publish (automatic)

When a `WorkflowClass` is published (see [Chapter 9](09-workflow-class-governance.md)), `WorkflowClassesController.Publish` automatically creates and publishes an `EventDefinition` for every event in the blueprint that doesn't already exist for the tenant.

## Referencing events in configuration

### State Machine transitions

```json
{ "fromState": "Pending", "eventId": "EVT-ORDER-APPROVED", "toState": "Approved" }
```

### Workflow steps

Use the `EventId` as the key in a step's `nextSteps` dictionary:

```csharp
new WorkflowStepDefinition("Review") {
    NextSteps = { { "EVT-ORDER-APPROVED", "FinanceStep" } }
}
```

## Listing registered events

```bash
curl -X GET "http://localhost:5183/api/admin/events" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
```

```json
[
  {
    "eventId": "EVT-ORDER-APPROVED",
    "displayName": "Order Approved",
    "description": "Triggered when a manager approves an order.",
    "entityType": "Order",
    "category": "Decision"
  }
]
```

## Publishing an event with a payload

**Endpoint:** `POST /api/events/publish`

```bash
curl -X POST "http://localhost:5183/api/events/publish" \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -d '{
    "tenantId": "11111111-1111-1111-1111-111111111111",
    "workflowInstanceId": "<WORKFLOW_INSTANCE_ID>",
    "eventType": "EVT-ORDER-APPROVED",
    "payload": {
      "approver": "Jane Doe",
      "reason": "Looks good",
      "amount": 150.00
    }
  }'
```

* **200 OK** → `"Event published"`
* **400 Bad Request** → processing failed (unregistered event, or no valid transition for the current step). Returned as RFC 7807 Problem Details.

Payloads are serialized to JSON and stored on the persisted `Event`'s metadata, so they show up later in the Admin timeline (see [Chapter 14](14-api-reference.md)):

```json
"timeline": [
  {
    "eventType": "EVT-ORDER-APPROVED",
    "summary": "Event: EVT-ORDER-APPROVED",
    "keyData": { "Payload": "{\"approver\":\"Jane Doe\",\"reason\":\"Looks good\",\"amount\":150}" }
  }
]
```

## Validation logic at publish time

1. **Strict check** — if the event string starts with `EVT-`, the system verifies it exists in the Registry. If missing: `400 Bad Request`, `"Event '{eventType}' is not registered"`.
2. **Transition validation** — even if the event is registered, if the current workflow step doesn't define a transition for it, the engine rejects it with a `400 Bad Request` describing the failed transition.
3. **Legacy/system support** — strings that do *not* start with `EVT-` (e.g. `"StartTodo"`, `"Default"`) bypass the strict registry check and rely entirely on the workflow engine's transition rules. Useful for internal system tasks, but should not be used for core domain logic going forward.

## Key concepts

* **Correlation**: if you omit `correlationId`, FlowOS automatically links the event to the target `workflowInstanceId`.
* **Persistence**: published events are saved to the `Events` table, subject to the caveats in [Chapter 15](15-known-limitations-and-gaps.md).

## Where to go next

* [Chapter 5 — Workflows & Versioning](05-workflows-and-versioning.md) to see events drive step transitions end-to-end.
* [Chapter 6 — Human Tasks & Decisions](06-human-tasks-and-decisions.md) for data-driven routing using event payloads.
