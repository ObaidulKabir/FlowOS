# 14. API Reference

## Base URL

`http://localhost:5183` for local development (`dotnet run`, see [Chapter 1](01-getting-started.md)).

## Authentication

There is currently no production identity provider wired in. `MockAuthMiddleware` turns two headers into a `ClaimsPrincipal` for development/testing:

* `x-tenant-id` — UUID of the tenant. Required by every controller that reads `ICurrentUser.TenantId`.
* `X-Mock-Role` — simulates a role claim. Required for any `[Authorize]`-protected controller (`WorkflowsController`, `WorkflowClassesController`); omitting it returns `401 Unauthorized`.

Header names are case-insensitive in ASP.NET Core, so `x-tenant-id` and `X-Tenant-ID` are equivalent.

## Safe retries: `Idempotency-Key`

`POST /api/workflows/start`, `POST /api/events/publish` and `POST /api/tasks/{id}/complete` accept an `Idempotency-Key` header (at most 200 characters, unique per tenant and operation). Send a new random value (for example a UUID) for each logical request, and send the same value again when you retry it:

| Situation | Response |
|---|---|
| First request with the key | Processed normally |
| Repeat after the first finished | The first result again (the same `workflowInstanceId` for start); nothing runs twice |
| Repeat while the first is still running | `409 Conflict`, `code: IDEMPOTENCY-IN-PROGRESS`; retry shortly |
| First request failed | A repeat with the same key is processed again |
| Header and body `idempotencyKey` differ, two header values, or a key over 200 characters | `400 Bad Request` |

The body field `idempotencyKey` still works; the header is preferred.

## Integration references (correlation, causation, external reference)

For systems that drive FlowOS, such as Learning OS:

* **Start** (`POST /api/workflows/start`): send `businessReference: { sourceSystem, externalEntityId }` to store your own identifier on the instance (at most 100 and 200 characters). The response contains `workflowInstanceId`, `workflowDefinitionId`, `workflowVersion` (the pinned definition), `correlationId`, `externalSourceSystem`, `externalReference` and `status`.
* **Find by your reference**: `GET /api/workflows?sourceSystem=learning-os&externalReference=recovery-case-123`.
* **Publish** (`POST /api/events/publish`): send `causationId` to record what caused the event. Every stored and outbox event carries the metadata `WorkflowInstanceId`, `WorkflowDefinitionId`, `WorkflowVersion`, and, when present, `CausationId` and `ExternalEntityId`.

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
| `GET /api/tasks` | The caller's inbox (`TaskDto[]`), ordered by `priority` (highest first), then `dueAtUtc` (earliest first; tasks without an SLA last), then `createdAt` (oldest first). Filters: `role`, `minPriority`, `dueBefore`. Paging: `skip`, `take` (1–500); the `X-Total-Count` header has the filtered total. |
| `GET /api/tasks/{id}` | Get a task by id (the id is the `WorkflowInstanceId`); `404` if missing. |
| `POST /api/tasks/{id}/complete` | Emits a generic `TaskCompleted` event; `400 Bad Request` if it can't be completed. |
| `PUT /api/workflows/{id}/priority` | Body `{"priority": 0–100 or null}`. Sets or clears the instance's priority (requires `workflow.start`). `400` outside 0–100, `404` for an unknown instance. |

`TaskDto`:

```json
{
  "taskId": "uuid", "workflowId": "uuid", "currentStep": "string",
  "requiredRole": "string", "status": "string", "relatedEntity": {},
  "priority": 0, "dueAtUtc": "timestamp or null", "createdAt": "timestamp",
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

## 3a. Change feed — `ChangesController` (`/api/changes`, `[Authorize]`)

An integrating system (for example Learning OS) pulls the events of its own tenant in the order the database stored them, instead of receiving pushes.

```
GET /api/changes?after=<cursor>&limit=100
```

* `after`: the `nextCursor` from the previous page, sent back unchanged. Leave it out to start from the beginning. A malformed cursor returns `400`.
* `limit`: 1–500, default 100.
* The response matches [`docs/schemas/change-feed.v1.schema.json`](schemas/change-feed.v1.schema.json). Version 1 only gains fields; removing or renaming one needs `v2`.

```json
{
  "schema": "flowos.change-feed/v1",
  "items": [
    {
      "sequence": 4182,
      "eventId": "…", "eventType": "EVT-ASSESSMENT-SUBMITTED", "eventVersion": 1,
      "occurredAt": "2026-10-27T09:12:44.512Z",
      "correlationId": "…", "causationId": "…",
      "workflowInstanceId": "…", "workflowDefinitionId": "…", "workflowVersion": 3,
      "externalSourceSystem": "learning-os", "externalReference": "recovery-case-123",
      "payload": { "score": 7 },
      "metadata": { "FromStep": "Review", "ToStep": "END" }
    }
  ],
  "nextCursor": "djE6NDE4Mg",
  "hasMore": false
}
```

**Guarantees and how to consume it**

* `sequence` strictly increases within a tenant's feed, so events of one workflow instance arrive in the order they were stored.
* **At-least-once.** Save `nextCursor` only after you have processed the page. If you crash first, you get the same page again; use `eventId` to ignore duplicates.
* **Settle window.** Events newer than `FlowOS:ChangeFeed:SettleSeconds` (default 5) are held back, together with everything after them, so a transaction that commits late is never skipped. `hasMore` can be `true` with an empty page; poll again shortly.
* `workflowInstanceId`, `workflowDefinitionId`, `workflowVersion` and the external reference are filled in for events that belong to a workflow instance (through `WorkflowInstanceId` metadata or the correlation ID).

**Signature.** When `FlowOS:ChangeFeed:SigningKey` is configured, every response has

```
X-FlowOS-Signature: t=<unix seconds>,v1=<hex HMAC-SHA256 of "<t>." + raw body>
```

Verify it over the raw response bytes before parsing, with a constant-time comparison, and reject a `t` older than a few minutes. Keep the key in a secret store, never in code or prompts. `FlowOS.Api.Http.ChangeFeedSignature.Verify` is a reference implementation.

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

* `POST /api/workflow-classes` — create a new private draft blueprint (`CreateWorkflowClassCommand`).
* `PUT /api/workflow-classes/{id}` — update an existing draft.
* `POST /api/workflow-classes/{id}/publish` — compile and publish draft to immutable versioned runtime status (`PublishWorkflowClassCommand`).
* `POST /api/workflow-classes/{id}/new-version?bump=Major|Minor|Build` — create a new draft copy bumping Major.Minor.Build (`Major`, `Minor`, or `Build`; `Patch` is an alias of `Build`). Body: `{ "changeLog": "..." }`. Links `PreviousVersionId`.
* `POST /api/workflow-classes/{id}/rollback` — safe rollback: deprecates the current version with a pointer to the previous version without interrupting running instances (`RollbackWorkflowClassCommand`).
* `GET /api/workflow-classes/by-name/{name}/version-tree` — returns full version history and lineage DAG for a workflow.
* `POST /api/workflow-classes/{id}/deprecate` — mark as deprecated with optional reason and migration target pointer.
* `POST /api/workflow-classes/{id}/copy` — clone a `Public` template into caller's tenant with version reset to `1.0.0`.
* `POST /api/workflow-classes/{id}/validate` — run authoritative validation without modifying anything.
* `POST /api/workflow-classes/lint` — structural advisory linting.
* `GET /api/workflow-classes?applicationId=` — only that application's workflow classes; `?unassigned=true` returns only those not in any application. Every workflow class has `applicationId` (or `null`).
* `POST /api/workflow-classes/{id}/application` — body `{ "applicationId": "<guid>" | null }`. Moves the workflow to an application (see section 12), or unassigns it with `null`. Every version of the workflow (the tenant's workflow classes with the same name) moves together; the response reports `versionsMoved`. `404 {code:"APP-NOTFOUND-001"}` if the application or workflow class is not in the caller's tenant. Allowed in any status: it does not change the blueprint.
* `POST /api/workflow-classes` also accepts an optional `applicationId` for the new draft. A new version (`new-version`) stays in its source's application; a `copy` from a `Public` template starts unassigned.

**Application-restricted keys (4/4).** A caller authenticated with an `isApplicationRestricted` key (section 12) is confined to its own application on every route above that names a workflow class `{id}`: a class in a different application, or an unassigned one, is `404` for that caller - the same response as a genuinely unknown id, so the key cannot distinguish "not found" from "not yours." `GET /api/workflow-classes` ignores the caller's `applicationId`/`unassigned` query and always returns only that application's classes. A new draft (`POST /api/workflow-classes`) is always placed in the key's own application (`403 {code:"APP-SCOPE-001"}` if the body names a different one), and moving a workflow between applications (`POST /api/workflow-classes/{id}/application`) is refused outright for such a key. An unrestricted key, and every other caller (a dashboard session, a platform administrator), is unaffected.

See the full lifecycle diagram and blueprint schema in [Chapter 9 — WorkflowClass Governance](09-workflow-class-governance.md#rest-api--verified-against-workflowclassescontroller).

---

## 9. Context bindings — `ContextBindingsController` (`/api/context-bindings`, `[Authorize]`)

* `POST /api/context-bindings` — create binding plus draft revision 1.
* `PUT /api/context-bindings/{id}/draft` — update the draft or create the next draft revision.
* `POST /api/context-bindings/{id}/validate` — run binding validation without mutation.
* `POST /api/context-bindings/{id}/activate` — materialize and activate an immutable runtime package.
* `POST /api/context-bindings/{id}/archive` — block new starts.
* `GET /api/context-bindings` and `GET /api/context-bindings/{id}` — tenant-scoped reads.
* `POST /api/context-bindings/simulate` — side-effect-free simulation of a saved `draft` or pinned `active` revision. The body accepts exactly one of `contextBindingId`/`contextType`, an initial source payload, simulated roles, ordered contextual events, and `maxSteps` (1–100).

`POST /api/workflows/start` accepts optional `contextBindingId`, `contextType`, and `businessReference`. A context selector cannot be combined with workflow definition, class, or name selectors. See [Chapter 17](17-workflow-context-bindings.md).

Simulation errors use stable `Code` values: `CTX-SIM-ARGUMENT` (`400`), `CTX-SIM-NOTFOUND` (`404`), `CTX-SIM-STATE` (`409`), `CTX-SIM-BINDING-INVALID` (`422`), and `CTX-SIM-PAYLOAD-INVALID` (`422`). A successful result always reports `sideEffectsSuppressed: true`.

---

## 10. Notifications — `NotificationsController` (`/api/notifications`)

* `GET /api/notifications` — history for the current tenant.
* `GET /api/notifications/stream` — Server-Sent Events stream.

Full detail: [Chapter 10 — Notifications](10-notifications.md).

---

## 11. Tenants and institution provisioning — `TenantsController` (`/api/tenants`)

Platform administration endpoints (an Admin of the platform tenant). An integrating system such as Learning OS uses a platform credential as its **service principal** to provision **one FlowOS tenant per institution** and keeps its own hierarchy (board → institution → campus) on its side (CR-14).

| Method & Route | Description |
|---|---|
| `POST /api/tenants` | Body `{name, externalTenantRef?, keyName?, scopes?, expiresInDays?}`. Creates the tenant and its first API key (returned once as `apiKey`). With `externalTenantRef`, a retry finds the existing tenant instead: `409 {code:"TENANT-EXISTS", tenantId}`. |
| `GET /api/tenants?externalRef=` | Finds the tenant mapped to the caller's reference (empty array if none). |
| `PUT /api/tenants/{id}/external-ref` | Body `{externalTenantRef}`; set or clear (`null`) the reference of an existing tenant. `409` if another tenant uses it. |
| `POST /api/tenants/{id}/keys` | A scoped key for acting inside that tenant (`scopes`, `expiresInDays`). Store it in a secret store. With `applicationId`, the key is issued for that application (see section 12): its `applicationName` follows the application's name and it is returned with `isApplicationRestricted: true`. Without it, `applicationName` is a free-text label as before. |
| `GET /api/tenants/{id}/keys?applicationId=` | Lists keys, optionally only one application's. Each key has `applicationId` (or `null`) and `isApplicationRestricted`. |

`externalTenantRef` is an opaque string (up to 200 characters, unique), for example `learning-os:board-dhaka/school-123`. A tenant key only works inside its own tenant; sending another tenant's id returns `403`.

To share one workflow template with many institutions, publish it as a **Public** workflow class (approved by a platform administrator) and start or bind it in each tenant.

## 12. Applications — `ApplicationsController` (`/api/applications`, `[Authorize]`)

An application is something the tenant builds on FlowOS. It groups that application's workflows and FlowOS API keys; one application usually has several workflows, and several keys over its life (production, a replacement during rotation). Assign a workflow with `POST /api/workflow-classes/{id}/application` (section 8). AI provider configuration, including model API keys, stays tenant-level. Every route works inside the caller's tenant.

| Method & Route | Description |
|---|---|
| `GET /api/applications` | The tenant's applications, ordered by name. Each has `id`, `name`, `description`, `createdAtUtc`, `updatedAtUtc`, `activeKeyCount` (keys not revoked or expired), and `workflowCount` (distinct workflows; all versions of one workflow count once). |
| `GET /api/applications/{id}` | One application, or `404 {code:"APP-NOTFOUND-001"}`. |
| `POST /api/applications` | Body `{name, description?}`. Names are unique per tenant, ignoring case (`409 {code:"APP-NAME-001"}`); a blank name or one over 150 characters is `400 {code:"APP-ARG-001"}`. |
| `PUT /api/applications/{id}` | Body `{name, description?}`. Renaming also updates `applicationName` on the application's keys. |
| `DELETE /api/applications/{id}` | `409 {code:"APP-INUSE-001"}` while the application has an active key, any workflow, or any agent prompt or profile binding (section 13); revoke the keys, move or unassign the workflows, and delete or move the bindings first. Revoked and expired keys are unlinked, then the application is removed. |

**Existing keys.** When applications were introduced, each distinct `applicationName` label already on a tenant's keys became an application, and those keys were linked to it. Keys still labelled `Default Application` were left unlinked (`applicationId: null`). Linked existing keys have `isApplicationRestricted: false`.

**`isApplicationRestricted`.** A key issued with `applicationId` is marked restricted to that application on the `WorkflowClassesController` surface (section 8): every `{id}`-scoped route (`GET`, `PUT`, `publish`, `submit`, `withdraw`, `validate`, `deprecate`, `DELETE`, `abandon`, `approve`, `copy`, `new-version`, `rollback`) treats a workflow class in a different application, or one not assigned to any application, as `404` for such a key. `GET /api/workflow-classes` ignores any `applicationId`/`unassigned` the caller passes and returns only that key's own application. `POST /api/workflow-classes` places a new draft in the key's application regardless of the body's `applicationId` (`403 {code:"APP-SCOPE-001"}` if it names a different one), and `POST /api/workflow-classes/{id}/application` - moving a workflow between applications - is refused outright (`403 {code:"APP-SCOPE-001"}`) for a restricted key. An unrestricted key (`isApplicationRestricted: false`) is unaffected and still works across its whole tenant, as is every other route (plugin bindings, tenant keys, instances, context bindings).

## 13. Agent Context bindings — `PluginBindingsController` (`/api/plugin-bindings`, `[Authorize]`)

Tenant-owned prompts (`bindingType: prompt`) and agent profiles (`bindingType: profile`) — see [Chapter 7](07-ai-agents-and-insights.md#tenant-owned-prompts-create--edit) — may belong to a tenant application instead of being tenant-shared. AI providers (`bindingType: agent`) and other binding types are always tenant-shared; the `applicationId` field described below rejects them.

| Method & Route | Description |
|---|---|
| `GET /api/plugin-bindings?applicationId=` | Only that application's prompt/profile bindings. `?shared=true` returns only tenant-shared bindings instead. Neither filter returns both kinds; every binding reports `applicationId` (`null` when tenant-shared). |
| `PUT /api/plugin-bindings` | Body gains an optional `applicationId`. Set on a `prompt` or `profile` binding, it is saved in that application instead of tenant-shared — a step in the application's workflows resolves it before a tenant-shared binding with the same alias. Any other `bindingType` with `applicationId` set is `400`. |
| `DELETE /api/plugin-bindings?bindingType=&sourceName=&applicationId=` | Deletes a `prompt` or `profile` binding in exactly the scope named — that application's (with `applicationId`) or tenant-shared (without). `400` for any other `bindingType`; `404` if no binding exists in that exact scope. |

Deleting an application (section 12) is refused while it still has a prompt or profile binding.

## Error format

Most error responses use RFC 7807 Problem Details, produced by `ApiExceptionFilterAttribute`:

| Exception | HTTP Status |
|---|---|
| `PolicyViolationException` | `403 Forbidden` |
| `ArgumentException` | `400 Bad Request` |
| `ConcurrencyConflictException` (another request changed the instance, or the same idempotency key is still running) | `409 Conflict`, with `code` = `CONCURRENT-UPDATE` or `IDEMPOTENCY-IN-PROGRESS` |
| `InvalidOperationException` | `400 Bad Request` |
| Unhandled | `500 Internal Server Error` |

```json
{ "title": "Policy Violation", "status": 403, "detail": "Policy 'DenyAll' denied execution: DenyAll policy is active." }
```

## Where to go next

* [Chapter 15 — Known Limitations & Gaps](15-known-limitations-and-gaps.md) for everything in this reference that is documented-but-not-fully-enforced.
