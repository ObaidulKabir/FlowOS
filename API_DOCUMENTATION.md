# FlowOS API Documentation

## Base URL
`http://localhost:5005` (Development)

## Overview
FlowOS API provides endpoints to manage workflows, tasks, events, and administrative views.

## Authentication
*Currently, the API uses a placeholder `CurrentUserService` that simulates a logged-in user with a fixed Tenant ID.*

### Headers
Most endpoints (especially Admin) require the following header to identify the tenant context:
*   `x-tenant-id`: UUID of the tenant (e.g., `11111111-1111-1111-1111-111111111111`)

---

## 1. Workflows
Endpoints for managing workflow instances.

### Get All Workflows
Retrieves a list of workflow instances for the current tenant.

*   **URL**: `/api/workflows`
*   **Method**: `GET`
*   **Parameters**:
    *   `status` (query, optional): Filter by status (e.g., `Running`, `Completed`). Defaults to `Running`.
*   **Response**: `200 OK`

```json
[
  {
    "id": "uuid",
    "definitionId": "uuid",
    "version": 1,
    "currentStepId": "string",
    "status": "Running",
    "correlationId": "uuid",
    "createdAt": "timestamp"
  }
]
```

### Start a Workflow
Starts a new instance of a workflow definition.

*   **URL**: `/api/workflows/start`
*   **Method**: `POST`
*   **Request Body**: `application/json`

You can start a workflow by **ID** OR by **Name**.

**Option A: By Name (Recommended)**
```json
{
  "tenantId": "uuid",
  "workflowName": "OrderApprovalWorkflow",
  "version": 1,
  "initialStepId": "Start",
  "correlationId": "uuid? (optional)"
}
```

**Option B: By Definition ID**
```json
{
  "tenantId": "uuid",
  "workflowDefinitionId": "uuid",
  "version": 1,
  "initialStepId": "string",
  "correlationId": "uuid? (optional)"
}
```

*   **Response**: `200 OK`

```json
{
  "workflowInstanceId": "uuid"
}
```

### Get Workflow Status
Retrieves the status of a specific workflow instance.

*   **URL**: `/api/workflows/{id}`
*   **Method**: `GET`
*   **Parameters**:
    *   `id` (path): The UUID of the workflow instance.
*   **Response**: `200 OK`

```json
{
  "id": "uuid",
  "status": "Running"
}
```

---

## 2. Tasks
Endpoints for retrieving and completing user tasks assigned by workflows.

### Get All Tasks
Retrieves a list of pending tasks for the current tenant.

*   **URL**: `/api/tasks`
*   **Method**: `GET`
*   **Response**: `200 OK`

```json
[
  {
    "taskId": "uuid",
    "workflowId": "uuid",
    "currentStep": "string",
    "requiredRole": "string",
    "status": "string",
    "agentInsights": [
      {
        "agentId": "string",
        "insight": "string",
        "contextObjective": "string",
        "createdAt": "2024-01-30T12:00:00Z"
      }
    ]
  }
]
```

### Get Task Details
Retrieves details for a specific task.

*   **URL**: `/api/tasks/{id}`
*   **Method**: `GET`
*   **Parameters**:
    *   `id` (path): The UUID of the task (Workflow Instance ID).
*   **Response**: `200 OK` (Same format as Task object above) or `404 Not Found`.

### Complete Task
Completes a task, triggering the transition to the next step in the workflow.

*   **URL**: `/api/tasks/{id}/complete`
*   **Method**: `POST`
*   **Parameters**:
    *   `id` (path): The UUID of the task/workflow instance.
*   **Response**:
    *   `200 OK`: `{ "success": true }`
    *   `400 Bad Request`: If the task cannot be completed or is not found.

---

## 3. Events
Endpoints for publishing external events into the system.

### Publish Event
Publishes an event to a running workflow instance, potentially triggering state transitions.

*   **URL**: `/api/events/publish`
*   **Method**: `POST`
*   **Request Body**: `application/json`

```json
{
  "tenantId": "uuid",
  "workflowInstanceId": "uuid",
  "eventType": "string",
  "correlationId": "uuid? (optional)"
}
```

*   **Response**:
    *   `200 OK`: `"Event published"`
    *   `400 Bad Request`: If processing failed.

---

## 4. Admin
Administrative endpoints for deep inspection.

### Get All Workflows (Admin)
Retrieves a summary of all workflows for the tenant, including definition names.

*   **URL**: `/api/admin/workflows`
*   **Method**: `GET`
*   **Response**: `200 OK`

```json
[
  {
    "id": "uuid",
    "definitionId": "uuid",
    "definitionName": "Order Approval Workflow",
    "version": 1,
    "currentStepId": "Start",
    "status": "Running",
    "correlationId": "uuid"
  }
]
```

### Get Workflow Detail
Retrieves comprehensive details about a workflow instance, including its timeline and audit history.

*   **URL**: `/api/admin/workflows/{id}`
*   **Method**: `GET`
*   **Parameters**:
    *   `id` (path): The UUID of the workflow instance.
*   **Response**: `200 OK`

```json
{
  "id": "uuid",
  "definitionId": "uuid",
  "definitionName": "string",
  "version": 1,
  "currentStepId": "string",
  "status": "string",
  "correlationId": "uuid",
  "createdAt": "timestamp",
  "timeline": [
    {
      "eventId": "uuid",
      "eventType": "string",
      "timestamp": "timestamp",
      "summary": "string",
      "keyData": {
        "key": "value"
      }
    }
  ]
}
```
