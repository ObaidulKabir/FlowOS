# 3. State Machines (The Law)

FlowOS enforces all entity state changes through **State Machines**. A State Machine defines the legal states and transitions for a business entity (e.g. `"Order"`). It is the system of record for legality: no workflow can move an entity to a state not defined here.

> **Rule:** No entity can exist in a state undefined by its State Machine, and no entity can move between states without a valid transition trigger.

## Definition structure (JSON configuration)

State machine configuration files live under `flowos-config/state-machines/` and are loaded by `ConfigurationLoader` on startup (or via `POST /api/admin/config/publish`).

```json
{
  "entityType": "Order",
  "initialState": "Created",
  "states": ["Created", "PendingApproval", "Approved", "Rejected"],
  "transitions": [
    { "fromState": "Created", "toState": "PendingApproval", "eventId": "EVT-ORDER-SUBMITTED" },
    { "fromState": "PendingApproval", "toState": "Approved", "eventId": "EVT-ORDER-APPROVED" },
    { "fromState": "PendingApproval", "toState": "Rejected", "eventId": "EVT-ORDER-REJECTED" }
  ]
}
```

> Use `eventId` (referencing a registered `EVT-*` id — see [Chapter 4](04-events-and-registry.md)), not the legacy `triggerEventType` field name used in older FlowOS configs.

## Validating a transition via the API

The `StateMachinesController` exposes a dedicated, side-effect-free validation endpoint. This is useful for testing business rules independently of a running workflow, for UI pre-validation, and for AI agent reasoning.

**Endpoint:** `POST /api/StateMachines/validate` (route matching is case-insensitive, so `/api/statemachines/validate` also works)

Request body (`ValidateTransitionRequest`):

```json
{ "entityType": "Order", "currentState": "Pending", "eventType": "EVT-ORDER-APPROVED" }
```

Requires an `x-tenant-id` header — the controller returns `400 Bad Request` without it.

### 1. Allowed transition

```bash
curl -X POST "http://localhost:5183/api/statemachines/validate" \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -d '{ "entityType": "Order", "currentState": "Pending", "eventType": "EVT-ORDER-APPROVED" }'
```

```json
{ "isAllowed": true, "reason": "Transition allowed.", "newState": "Approved", "resultType": "Allowed" }
```

### 2. Denied transition (already in target state)

```bash
curl -X POST "http://localhost:5183/api/statemachines/validate" \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -d '{ "entityType": "Order", "currentState": "Approved", "eventType": "EVT-ORDER-APPROVED" }'
```

```json
{ "isAllowed": false, "reason": "Event 'EVT-ORDER-APPROVED' is not valid for current state 'Approved'.", "newState": null, "resultType": "Denied" }
```

### 3. Ignored transition (event not modeled by this state machine)

```json
{ "isAllowed": true, "reason": "Event 'EVT-UNKNOWN-EVENT' is not defined in this State Machine.", "newState": null, "resultType": "Ignored" }
```

> **Read this carefully:** `isAllowed: true` with `resultType: "Ignored"` means the State Machine does not *block* the event — but it also does not cause a state transition. Don't interpret "allowed" as "state will change". This distinction matters a great deal in [Chapter 15](15-known-limitations-and-gaps.md) — the runtime event-publishing path does not currently call this same validation when advancing a *workflow*.

### 4. Invalid current state

```json
{ "isAllowed": false, "reason": "Current state 'Shipped' is not valid for this definition.", "newState": null, "resultType": "Denied" }
```

## Strategic uses of the validation endpoint

1. **Developer training** — verify your understanding of the Law layer before writing a Workflow that assumes a transition is legal.
2. **UI pre-validation** — disable a button, or show "Cannot approve an order that is already Shipped" before the user even clicks.
3. **Agent reasoning** — an AI agent can validate a candidate action and explain legality to a user without any side effects.

## Bootstrapping a State Machine in code

```csharp
var orderStateMachine = new StateMachineDefinition(
    entityType: "Order",
    initialState: "Created"
);
orderStateMachine.AddState("PendingApproval");
orderStateMachine.AddTransition("Created", "PendingApproval", "EVT-ORDER-SUBMITTED");
```

## Where to go next

* [Chapter 4 — Events & the Event Registry](04-events-and-registry.md) to define the vocabulary that drives transitions.
* [Chapter 15 — Known Limitations](15-known-limitations-and-gaps.md) for the verified gap between State Machine validation and workflow-driven event publishing.
