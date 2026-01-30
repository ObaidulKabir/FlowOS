# Event API Curl Guide

This guide explains how to interact with the FlowOS Event API using `curl`. Events drive workflow transitions and capture system activity.

## Prerequisites

- FlowOS API running on `http://localhost:5005`
- Tenant ID: `11111111-1111-1111-1111-111111111111` (Default)

## 1. List Registered Events

View all event definitions available in the system.

```bash
curl -X GET "http://localhost:5005/api/admin/events" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
```

**Response Example:**
```json
[
  {
    "eventId": "EVT-ORDER-APPROVED",
    "displayName": "Order Approved",
    "description": "Triggered when a manager approves an order.",
    "entityType": "Order",
    "category": "Decision"
  },
  ...
]
```

## 2. Publish an Event with Payload

Events can carry data payload which is stored in the event history.

**Scenario:** Approve an order with a reason and approver name.

1.  **Start a Workflow** (to get an instance ID):
    ```bash
    curl -X POST "http://localhost:5005/api/workflows/start" \
    -H "Content-Type: application/json" \
    -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
    -d '{"workflowName": "OrderApprovalWorkflow", "version": 1, "initialStepId": "ReviewStep"}'
    ```

2.  **Publish Event:**
    ```bash
    curl -X POST "http://localhost:5005/api/events/publish" \
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

**Response:** `"Event published"`

## 3. Verify Event Data

Check the workflow timeline to see the recorded event and its payload.

```bash
curl -X GET "http://localhost:5005/api/admin/workflows/<WORKFLOW_INSTANCE_ID>" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
```

**Expected Response (Snippet):**
```json
"timeline": [
  {
    "eventId": "...",
    "eventType": "EVT-ORDER-APPROVED",
    "summary": "Event: EVT-ORDER-APPROVED",
    "keyData": {
      "Payload": "{\"approver\":\"Jane Doe\",\"reason\":\"Looks good\",\"amount\":150}"
    }
  }
]
```

## Key Concepts

- **Correlation:** If you don't provide a `correlationId`, the system automatically links the event to the target `workflowInstanceId`.
- **Persistence:** All published events are saved to the `Events` table, even if they don't trigger a state transition (though currently only successful workflow advances are persisted by the handler).
- **Payloads:** Payloads are serialized to JSON and stored in the event's `Metadata`.

## Troubleshooting

- **Event not showing in timeline:** Ensure the workflow exists and the event caused a valid transition (or logic was updated to save non-transitioning events).
- **400 Bad Request:** Check JSON syntax.
