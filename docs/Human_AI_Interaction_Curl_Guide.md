# Human & AI Interaction Guide

This guide explains how to interact with **Human Tasks** and **AI Agent Decisions** in FlowOS using `curl`.

## 1. Human Task Completion

Human tasks are typically steps in a workflow that require manual approval or action. When a user completes a task, the system:
1. Records a `TaskCompleted` event.
2. Correlates it with the Workflow Instance.
3. Advances the Workflow State Machine.

### A. Complete a Task

**Endpoint:** `POST /api/tasks/{workflowInstanceId}/complete`

> **Note:** This endpoint triggers a generic `TaskCompleted` event. Use this for linear steps where completion automatically moves to the next step. For **Decision Steps** (e.g., Approve vs Reject), use the Event Publication endpoint below to send the specific outcome event.

**Request:**
```bash
# Replace {workflow_id} with your actual Workflow Instance ID
curl -X POST "http://localhost:5000/api/tasks/{workflow_id}/complete" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-ID: 3fa85f64-5717-4562-b3fc-2c963f66afa6" \
  -H "X-User-ID: user-123" \
  -d '{
    "correlationId": "{workflow_id}" 
  }'
```

### B. Submit a Decision (Approval/Rejection)

For tasks that require a specific outcome (like "Approve" or "Reject"), publish the corresponding event explicitly.

**Endpoint:** `POST /api/events/publish`

**Request:**
```bash
curl -X POST "http://localhost:5000/api/events/publish" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-ID: 3fa85f64-5717-4562-b3fc-2c963f66afa6" \
  -d '{
    "workflowInstanceId": "{workflow_id}",
    "eventType": "EVT-ORDER-APPROVED", 
    "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
  }'
```

**Response:**
```json
{
  "success": true
}
```

---

## 2. AI Agent Decisions (Insights)

AI Agents can participate in workflows by observing state and generating "Insights". These are recorded as `AgentInsightGenerated` events and projected into a dedicated Read Model for the UI.

### A. Publish an Agent Insight

**Endpoint:** `POST /api/agents/insight`

**Request:**
```bash
# Replace {workflow_id} with your actual Workflow Instance ID
curl -X POST "http://localhost:5000/api/agents/insight" \
  -H "Content-Type: application/json" \
  -H "X-Tenant-ID: 3fa85f64-5717-4562-b3fc-2c963f66afa6" \
  -d '{
    "workflowInstanceId": "{workflow_id}",
    "agentId": "Risk-Analyzer-Bot-01",
    "insight": "Transaction risk score is 85/100. Manual review recommended.",
    "contextObjective": "Risk Assessment"
  }'
```

**Response:**
```json
{
  "success": true,
  "message": "Agent insight recorded."
}
```

---

## 3. Verification (Admin Timeline)

You can verify that both Human and AI events are correctly recorded and merged into the timeline.

**Endpoint:** `GET /api/admin/workflows/{workflow_id}/detail`

**Request:**
```bash
curl -X GET "http://localhost:5000/api/admin/workflows/{workflow_id}/detail" \
  -H "X-Tenant-ID: 3fa85f64-5717-4562-b3fc-2c963f66afa6"
```

**Expected Output (Timeline):**
The `timeline` array should contain entries for:
- `TaskCompleted`: Summary "Task completed by user"
- `AgentInsightGenerated`: Summary "Agent Risk-Analyzer-Bot-01 suggested: ..."

```json
{
  "timeline": [
    {
      "eventType": "AgentInsightGenerated",
      "summary": "Agent Risk-Analyzer-Bot-01 suggested: Transaction risk score is 85/100...",
      "keyData": {
        "Agent": "Risk-Analyzer-Bot-01",
        "Objective": "Risk Assessment"
      }
    },
    {
      "eventType": "TaskCompleted",
      "summary": "Task completed by user",
      "keyData": {
        "TaskId": "...",
        "UserId": "..."
      }
    }
  ]
}
```
