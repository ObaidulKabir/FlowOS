# Workflow API Curl Guide

This guide explains how to interact with the FlowOS Workflow API using `curl`.

## Prerequisites

- FlowOS API running on `http://localhost:5005`
- Tenant ID: `11111111-1111-1111-1111-111111111111` (Default)

## 1. Start a Workflow

To start an instance of the `OrderApprovalWorkflow`. We start at `ReviewStep` to simulate a human task scenario.

```bash
curl -X POST "http://localhost:5005/api/workflows/start" \
-H "Content-Type: application/json" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
-d '{"workflowName": "OrderApprovalWorkflow", "version": 1, "initialStepId": "ReviewStep"}'
```

**Response:**
```json
{"workflowInstanceId":"<GUID>"}
```

## 2. Check Workflow Status

Use the returned `workflowInstanceId` to check the status.

**User API (Basic Status):**
```bash
curl -X GET "http://localhost:5005/api/workflows/<WORKFLOW_INSTANCE_ID>" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
```

**Admin API (Detailed Status & Step):**
```bash
curl -X GET "http://localhost:5005/api/admin/workflows/<WORKFLOW_INSTANCE_ID>" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
```

**Expected Result:** `currentStepId` should be `"ReviewStep"`.

## 3. Approve the Order (Publish Event)

To advance the workflow from `ReviewStep` to `FinanceStep`, publish the `EVT-ORDER-APPROVED` event.

```bash
curl -X POST "http://localhost:5005/api/events/publish" \
-H "Content-Type: application/json" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
-d '{
  "tenantId": "11111111-1111-1111-1111-111111111111",
  "workflowInstanceId": "<WORKFLOW_INSTANCE_ID>",
  "eventType": "EVT-ORDER-APPROVED"
}'
```

**Response:** `"Event published"`

## 4. Verify Transition

Check the status again using the Admin API.

```bash
curl -X GET "http://localhost:5005/api/admin/workflows/<WORKFLOW_INSTANCE_ID>" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
```

**Expected Result:** `currentStepId` should now be `"FinanceStep"`.

## 5. Complete the Workflow (Auto-Advance)

The `FinanceStep` is configured to automatically advance to `EndStep` (via a "Default" transition). 
Since the engine now supports auto-advancement for "Default" transitions, you may find the workflow has already moved to `EndStep` if you check again.

If it hasn't (e.g., if auto-advance is disabled), you can manually trigger it:

```bash
curl -X POST "http://localhost:5005/api/events/publish" \
-H "Content-Type: application/json" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
-d '{
  "tenantId": "11111111-1111-1111-1111-111111111111",
  "workflowInstanceId": "<WORKFLOW_INSTANCE_ID>",
  "eventType": "Default"
}'
```

**Final Result:** `currentStepId` should be `"EndStep"`.

## Alternative Path: Reject Order

You can also test the rejection path. Start a new workflow instance and send `EVT-ORDER-REJECTED` instead.

1. **Start Workflow:** (Same as Step 1)
2. **Publish Rejection:**

```bash
curl -X POST "http://localhost:5005/api/events/publish" \
-H "Content-Type: application/json" \
-H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
-d '{
  "tenantId": "11111111-1111-1111-1111-111111111111",
  "workflowInstanceId": "<NEW_WORKFLOW_INSTANCE_ID>",
  "eventType": "EVT-ORDER-REJECTED"
}'
```

**Result:** The workflow should transition directly to `EndStep`.

## Troubleshooting

- **400 Bad Request:** Check JSON syntax and ensure `workflowInstanceId` is correct.
- **Workflow not found:** Ensure you are using the correct Tenant ID header.
- **Event processing failed:** Ensure the workflow is in the correct step (`ReviewStep`) before sending the event.
