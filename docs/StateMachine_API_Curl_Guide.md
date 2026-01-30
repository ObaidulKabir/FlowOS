# State Machine API Curl Guide

This guide explains how to validate State Machine transitions using `curl`. This is useful for testing business logic rules independently of workflows.

## Prerequisites

- FlowOS API running on `http://localhost:5005`
- Tenant ID: `11111111-1111-1111-1111-111111111111` (Default)

## 1. Validate an Allowed Transition

Test a valid transition defined in the State Machine (e.g., `Pending` -> `Approved`).

```bash
curl -X POST "http://localhost:5005/api/statemachines/validate" \
-H "Content-Type: application/json" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
-d '{
  "entityType": "Order",
  "currentState": "Pending",
  "eventType": "EVT-ORDER-APPROVED"
}'
```

**Response:**
```json
{
  "isAllowed": true,
  "reason": "Transition allowed.",
  "newState": "Approved",
  "resultType": "Allowed"
}
```

> **Note:** `isAllowed: true` with `resultType: Ignored` means the State Machine does not block the event, but it does not cause a state transition. This prevents misinterpretation that "Allowed means state will change".

## 2. Validate a Denied Transition

Test a transition that is explicitly invalid (e.g., trying to approve an already approved order).

```bash
curl -X POST "http://localhost:5005/api/statemachines/validate" \
-H "Content-Type: application/json" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
-d '{
  "entityType": "Order",
  "currentState": "Approved",
  "eventType": "EVT-ORDER-APPROVED"
}'
```

**Response:**
```json
{
  "isAllowed": false,
  "reason": "Event 'EVT-ORDER-APPROVED' is not valid for current state 'Approved'.",
  "newState": null,
  "resultType": "Denied"
}
```

## 3. Validate an Ignored Transition

Test an event that is not defined in the State Machine at all. The State Machine should ignore it (allowing the Workflow to handle it if it wants).

> **Future Compatibility:** If strict event registry mode is enabled in future phases, unknown events may be rejected before reaching the State Machine.

```bash
curl -X POST "http://localhost:5005/api/statemachines/validate" \
-H "Content-Type: application/json" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
-d '{
  "entityType": "Order",
  "currentState": "Pending",
  "eventType": "EVT-UNKNOWN-EVENT"
}'
```

**Response:**
```json
{
  "isAllowed": true,
  "reason": "Event 'EVT-UNKNOWN-EVENT' is not defined in this State Machine.",
  "newState": null,
  "resultType": "Ignored"
}
```

## 4. Validate an Invalid State

Test validation when the current state provided does not exist in the State Machine definition (e.g., `Shipped` is not a valid state for `Order`).

```bash
curl -X POST "http://localhost:5005/api/statemachines/validate" \
-H "Content-Type: application/json" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
-d '{
  "entityType": "Order",
  "currentState": "Shipped",
  "eventType": "EVT-ORDER-APPROVED"
}'
```

**Response:**
```json
{
  "isAllowed": false,
  "reason": "Current state 'Shipped' is not valid for this definition.",
  "newState": null,
  "resultType": "Denied"
}
```

## Strategic Usage Guide

This API is not just for backend testing. It enables robust application behavior:

1.  **Developer Training:** Use this to verify your understanding of the "Law Layer". If the State Machine denies it, do not write a Workflow that attempts it.
2.  **UI Pre-Validation:** Frontends can call this API to disable buttons or show explanations (e.g., "Cannot approve an order that is already Shipped") before the user even clicks.
3.  **Agent Reasoning:** AI Agents can use this to validate candidate actions and explain legality to users (e.g., "Approval is denied because the order is already approved.").

## Troubleshooting

- **400 Bad Request:** Check JSON syntax.
- **State Machine not found:** Ensure `entityType` matches a loaded State Machine configuration.
- **Invalid State:** Ensure `currentState` is one of the states defined in the configuration.
