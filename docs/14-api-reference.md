# 14. API Reference

## Base URL

`http://localhost:5183` for local development (`dotnet run`, see [Chapter 1](01-getting-started.md)).

## Authentication

There is currently no production identity provider wired in. `MockAuthMiddleware` turns two headers into a `ClaimsPrincipal` for development/testing:

* `x-tenant-id` — UUID of the tenant. Required by every controller that reads `ICurrentUser.TenantId`.
* `X-Mock-Role` — simulates a role claim. Required for any `[Authorize]`-protected controller (`WorkflowsController`, `WorkflowClassesController`); omitting it returns `401 Unauthorized`.

Header names are case-insensitive in ASP.NET Core, so `x-tenant-id` and `X-Tenant-ID` are equivalent.

---

## 1. Workflows — `WorkflowsController` (`/api/workflows`, `[Authorize]`)

### Start a workflow

`POST /api/workflows/start` — body: `StartWorkflowCommand`

```json
{
  "tenantId": "11111111-1111-1111-1111-111111111111",
  "workflowName": "OrderApprovalWorkflow",
  "version": 1,
  "initialStepId": "Start",
  "correlationId": null
}
```

You can start by `workflowName` **or** `workflowDefinitionId` — omit `version` to resolve the latest. `200 OK` → `{ "workflowInstanceId": "<guid>" }`. Requires capability `workflow.start` (see [Chapter 8](08-security-roles-and-policies.md)); a `PolicyViolationException` bubbles up as `403 Forbidden`.

### List workflows

`GET /api/workflows?status=Running` — optional `status` filter (`Running`, `Waiting`, `Completed`, `Failed`, from `WorkflowInstanceStatus`). Returns `401 Unauthorized` if `x-tenant-id` is missing/empty. Response: `List<WorkflowSummaryDto>`.

```json
[
  {
    "id": "uuid", "definitionId": "uuid", "workflowId": "uuid",
    "workflowClassId": "uuid", "workflowClassName": "string",
    "version": 1, "currentStepId": "string", "currentStep": "string",
    "status": "Running", "correlationId": "uuid",
    "createdAt": "timestamp", "completedAt": null
  }
]
```

### Get a workflow by id

`GET /api/workflows/{id}` → `WorkflowInstanceResponseDto`, or `404 Not Found`.

```json
{ "workflowId": "uuid", "workflowClassId": "uuid", "workflowClassName": "string", "correlationId": "string", "currentStep": "string", "status": "string", "createdAt": "timestamp", "completedAt": null }
```

---

## 2. Tasks — `TasksController` (`/api/tasks`)

| Method & Route | Description |
|---|---|
| `GET /api/tasks` | List pending tasks (`TaskDto[]`) for the current tenant. |
| `GET /api/tasks/{id}` | Get a task by id (the id is the `WorkflowInstanceId`); `404` if missing. |
| `POST /api/tasks/{id}/complete` | Emits a generic `TaskCompleted` event; `400 Bad Request` if it can't be completed. |

`TaskDto`:

```json
{
  "taskId": "uuid", "workflowId": "uuid", "currentStep": "string",
  "requiredRole": "string", "status": "string", "relatedEntity": {},
  "agentInsights": [
    { "agentId": "string", "insight": "string", "contextObjective": "string", "createdAt": "timestamp" }
  ]
}
```

---

## 3. Events — `EventsController` (`/api/Events`, case-insensitive)

`POST /api/events/publish` — body: `PublishEventCommand`

```json
{
  "tenantId": "11111111-1111-1111-1111-111111111111",
  "workflowInstanceId": "uuid",
  "eventType": "EVT-ORDER-APPROVED",
  "correlationId": null,
  "payload": { "Amount": 1000, "Category": "IT" }
}
```

* `200 OK` → `"Event published"`
* `400 Bad Request` → event not registered, or no valid transition from the current step for this event (details in the response body).

`tenantId` is actually resolved from the `x-tenant-id` header first, falling back to the body's `tenantId` only if the header is absent — see [Chapter 4](04-events-and-registry.md).

---

## 4. State Machines — `StateMachinesController` (`/api/StateMachines`)

`POST /api/statemachines/validate` — body: `ValidateTransitionRequest { entityType, currentState, eventType }`; requires `x-tenant-id` (else `400`). Response: `ValidateTransitionResult { isAllowed, reason, newState, resultType }` where `resultType` is one of `Allowed`, `Denied`, `Ignored`. Full walkthrough with all four response scenarios: [Chapter 3](03-state-machines.md).

---

## 5. Admin — `AdminController` (`/api/admin`) — read-only + config publish

| Method & Route | Description |
|---|---|
| `POST /api/admin/config/publish` | Reloads all JSON config under `flowos-config/` for the current tenant (searches several relative paths for the folder). |
| `GET /api/admin/workflows` | All workflow instances for the tenant (`AdminWorkflowSummaryDto[]`, includes `definitionName`). |
| `GET /api/admin/workflows/{id}` | Full detail + audit timeline (`AdminWorkflowDetailDto`); `404` if missing. |
| `GET /api/admin/state-machines` | All loaded State Machine definitions. |
| `GET /api/admin/state-machines/{entityType}` | A specific State Machine definition; `404` if not found. |
| `GET /api/admin/policies` | All policies for the tenant (`AdminPolicyDto[]`). |
| `GET /api/admin/events` | The registered event vocabulary (`AdminEventDefinitionDto[]`). |

`AdminWorkflowDetailDto`:

```json
{
  "id": "uuid", "definitionId": "uuid", "definitionName": "string", "version": 1,
  "currentStepId": "string", "status": "string", "correlationId": "uuid", "createdAt": "timestamp",
  "timeline": [
    { "eventId": "uuid", "eventType": "string", "timestamp": "timestamp", "summary": "string", "keyData": { "key": "value" } }
  ]
}
```

> **Note:** `AdminController` has no `[Authorize]` attribute (there's a commented-out `// [Authorize(Roles = "Admin")] // TODO: Add real auth`) — anyone who can reach the API and set an `x-tenant-id` header can currently call every Admin endpoint. Don't expose this port publicly without adding real authorization. Tracked in [Chapter 15](15-known-limitations-and-gaps.md).

---

## 6. Roles & Policies — see [Chapter 8](08-security-roles-and-policies.md) for full walkthroughs

* `POST /api/roles`, `POST /api/roles/{id}/capabilities`, `GET /api/roles/{id}` — `RolesController`.
* `POST /api/policies`, `GET /api/policies/{id}` — `PoliciesController`.

---

## 7. Agents — `AgentsController` (`/api/agents`)

`POST /api/agents/insight` — body: `PublishInsightDto { workflowInstanceId, agentId, insight, contextObjective, correlationId }`. `200 OK` → `{ success: true, message: "Agent insight recorded." }`; `404` if the workflow instance doesn't exist. Full walkthrough: [Chapter 7](07-ai-agents-and-insights.md).

---

## 8. WorkflowClasses (design-time governance) — `WorkflowClassesController` (`/api/workflow-classes`, `[Authorize]`)

See the full endpoint table, lifecycle diagram, and blueprint schema in [Chapter 9 — WorkflowClass Governance](09-workflow-class-governance.md#rest-api--verified-against-workflowclassescontroller).

---

## 9. Notifications — `NotificationsController` (`/api/notifications`)

* `GET /api/notifications` — history for the current tenant.
* `GET /api/notifications/stream` — Server-Sent Events stream.

Full detail: [Chapter 10 — Notifications](10-notifications.md).

---

## Error format

Most error responses use RFC 7807 Problem Details, produced by `ApiExceptionFilterAttribute`:

| Exception | HTTP Status |
|---|---|
| `PolicyViolationException` | `403 Forbidden` |
| `ArgumentException` | `400 Bad Request` |
| `InvalidOperationException` | `400 Bad Request` |
| Unhandled | `500 Internal Server Error` |

```json
{ "title": "Policy Violation", "status": 403, "detail": "Policy 'DenyAll' denied execution: DenyAll policy is active." }
```

## Where to go next

* [Chapter 15 — Known Limitations & Gaps](15-known-limitations-and-gaps.md) for everything in this reference that is documented-but-not-fully-enforced.
