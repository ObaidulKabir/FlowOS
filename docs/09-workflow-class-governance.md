# 9. WorkflowClass Governance

`WorkflowClass` is FlowOS's higher-level, governed unit of authoring: a versioned "configuration pack" that bundles the **Vocabulary** (Events), the **Law** (State Machine), the **Orchestration** (Workflow), and the **Governance** (Roles & Capabilities) of a business process into one artifact.

## Mental model

Think of `WorkflowClass` as a **Class** in programming, and a running workflow instance as an **Object**:

* **WorkflowClass** — the blueprint (`WorkflowClassBlueprint`, authored as JSON or via the Builder).
* **Runtime Definition** — the compiled, executable `WorkflowDefinition` the engine actually runs, produced automatically when you `Publish` a WorkflowClass.

`WorkflowClass` is **configuration only** — it never executes directly, and it never grants runtime authority by itself.

## The blueprint shape (verified against `FlowOS.Domain.Blueprints.WorkflowClassBlueprint`)

```csharp
public record WorkflowClassBlueprint
{
    public List<EventBlueprint> Events { get; init; }
    public StateMachineBlueprint StateMachine { get; init; }
    public WorkflowBlueprint Workflow { get; init; }
    public List<RoleBlueprint> Roles { get; init; }
    public List<CapabilityBlueprint> Capabilities { get; init; }
}
```

> **Field naming note:** the real C# properties use `EventId` (not `EventType`) throughout, and `StepBlueprint` has **no** `Label` or `Config` field. A couple of older MCP-focused documents in this repository used `EventType`/`Label`/`Config` in examples — those were aspirational/incorrect and have been corrected in this guide and in [Chapter 15](15-known-limitations-and-gaps.md#the-mcp-schema-tool-describes-a-shape-that-doesnt-match-the-real-blueprint). The JSON below is the **verified, accurate** shape (property names are serialized camelCase by default).

### 1. Events (vocabulary & data)

| Property | Type | Description |
|---|---|---|
| `eventId` | string | Unique id, e.g. `EVT-SUBMIT` |
| `name` | string | Human-readable name |
| `description` | string | Documents intent |
| `category` | enum | `System`, `Human`, `Decision`, `Agent` |
| `isTerminal` | bool | Whether this event concludes the process |
| `payloadSchema` | string? | Optional JSON Schema for payload validation |

### 2. StateMachine (the Law)

| Property | Type | Description |
|---|---|---|
| `entityType` | string | The domain entity being modeled (e.g. `Expense`) |
| `initialState` | string | Starting state |
| `states` | string[] | All possible states |
| `transitions` | Transition[] | `{ fromState, toState, eventId }` |

### 3. Workflow (the Work)

| Property | Type | Description |
|---|---|---|
| `startStepId` | string | Entry point step |
| `steps` | Step[] | See below |

**Step:**

| Property | Type | Description |
|---|---|---|
| `stepId` | string | Unique within the workflow |
| `stepType` | enum | `Command`, `SystemTask`, `HumanTask`, `Timer`, `Decision`, `End` |
| `nextSteps` | Dictionary<string,string> | `EventId` → `NextStepId` (or `"END"`) |
| `requiredRoles` | string[] | Roles allowed to perform this step |
| `conditions` | Dictionary<string,string> | Decision-step routing expressions → `NextStepId` |

### 4. Roles & Capabilities (governance)

| Property | Type | Description |
|---|---|---|
| `name` | string | Role name (e.g. `Manager`) |
| `description` | string | Role responsibility |
| `grantedCapabilities` | string[] | e.g. `event.publish.EVT-APPROVE` |

### Advanced behaviors

* **Auto-advance**: steps without human intervention auto-progress via a `"Default"` transition once their logic completes. True SLA-based escalation is modeled with explicit `EVT-TASK-OVERDUE` events, not implicit timers.
* **Human action**: `HumanTask` steps pause execution (`Status: Waiting`) until a user with a required role emits the right event.
* **AI reasoning**: agents emit `EVT-AGENT-INSIGHT`-style events; they never directly cause state changes unless a human confirms the resulting event (see [Chapter 7](07-ai-agents-and-insights.md)).

## Full example: Expense Approval

This blueprint is **validator-passing** — it was traced step-by-step through every rule in `WorkflowClassValidator.Validate` (note the required `capabilities` array; omitting it while a role grants a matching capability is the single most common authoring mistake — see `GOV-001` below).

```json
{
  "events": [
    { "eventId": "EVT-SUBMIT", "name": "Submit", "category": "Human" },
    { "eventId": "EVT-APPROVE", "name": "Approve", "category": "Human", "payloadSchema": "{ \"type\": \"object\", \"properties\": { \"comment\": { \"type\": \"string\" } } }" }
  ],
  "stateMachine": {
    "initialState": "Draft",
    "states": ["Draft", "Pending", "Approved"],
    "transitions": [
      { "fromState": "Draft", "toState": "Pending", "eventId": "EVT-SUBMIT" },
      { "fromState": "Pending", "toState": "Approved", "eventId": "EVT-APPROVE" }
    ]
  },
  "workflow": {
    "startStepId": "SubmitStep",
    "steps": [
      { "stepId": "SubmitStep", "stepType": "Command", "nextSteps": { "EVT-SUBMIT": "ApproveStep" } },
      { "stepId": "ApproveStep", "stepType": "HumanTask", "requiredRoles": ["Manager"], "nextSteps": { "EVT-APPROVE": "END" } }
    ]
  },
  "roles": [
    { "name": "Manager", "grantedCapabilities": ["event.publish.EVT-APPROVE"] }
  ],
  "capabilities": [
    { "code": "event.publish.EVT-APPROVE", "description": "Publish the approve event" }
  ]
}
```

## Validation rules — verified against `WorkflowClassValidator.Validate` (the actual error codes it emits)

| Code(s) | Category | Rule |
|---|---|---|
| `STR-000`, `STR-001`, `STR-002` | Structural | Definition/Name/Version must be present. |
| `WF-STR-001`–`WF-STR-004` | WorkflowStructure | `InitialState`, at least one step, and every step's `StepId`/`StepType` must be non-empty. |
| `EVT-SCHEMA-001` | Events | If `payloadSchema` is set, it must parse as valid JSON. |
| `CON-001`–`CON-003` | Consistency | Every StateMachine transition's `eventId`/`fromState`/`toState` must reference a **declared** event/state. |
| `WF-COMP-000`, `WF-COMP-001` | WorkflowCompleteness | `startStepId` must be set and must resolve to a real step. |
| `WF-COMP-004` | WorkflowCompleteness | Every step must be reachable from `startStepId` (BFS over `nextSteps`/`conditions`). |
| `WF-COMP-002` | WorkflowCompleteness | Every non-`End` step needs at least one exit path (`nextSteps` or, for `Decision`, `conditions`). |
| `WF-STRUCT-005` | WorkflowStructure | An `End`-type step must **not** declare `nextSteps`. |
| `CON-004`, `CON-005` | Consistency | Every `nextSteps`/`conditions` target must resolve to a defined `StepId` (or `"END"`); every `nextSteps` key must be a declared event (or the literal `"Default"`). |
| `WF-VAL-001`, `WF-VAL-002` | StepValidation | `Decision` steps need ≥1 condition; `HumanTask` steps need ≥1 exit path. |
| `GOV-001` | Governance | A role's `grantedCapabilities` must all appear in the top-level `capabilities` array. |

*Example error:* `CON-004: Step 'Working' references unknown NextStep 'Finished'`.

Validation runs on Draft creation/update (`_manager.CreateDraft` / `ValidateOnly`) and again on Publish (`_manager.Publish`), so a broken graph can never silently become non-executable configuration.

> ⚠️ **"Law" is not statically enforced at Draft/Publish time — nor, today, at runtime either.** You'll notice there's no rule above that cross-checks Workflow step transitions against the StateMachine's actual state graph — the validator source has a `// 3. Law Validation` block that is explicitly a stub/comment today, not an implemented check (it only re-confirms that referenced events exist, which `CON-001`/`CON-005` already do). This lines up with a deeper, separately-verified runtime gap: the live `PublishEventCommand` handler advances workflows via `WorkflowEngine.Advance()` without consulting the entity's `StateMachineDefinition` at all, so an illegal-per-the-State-Machine transition is not blocked automatically at either authoring time or execution time today. See [Chapter 15](15-known-limitations-and-gaps.md#the-runtime-event-publishing-path-does-not-enforce-state-machine-rules) for the full gap writeup and the regression tests that prove it.

## Lifecycle

```
Draft ──publish──▶ Published (Private) ──submit──▶ Shared ──approve──▶ Public
  ▲                      │                  │
  └──── withdraw ────────┘                  │
                                        deprecate/abandon
```

* **Draft** — editable, not executable, visible only to the owning tenant.
* **Published (Private)** — immutable; compiled into a runtime `WorkflowDefinition`; visible only to the owning tenant.
* **Shared** — submitted for admin review; read-only.
* **Public** — approved global template, visible to all tenants; must be **copied** before it can be executed (public templates are never executed in place).
* **Deprecated / Abandoned** — terminal states; no new instances or copies.

## REST API — verified against `WorkflowClassesController`

All routes are rooted at `/api/workflow-classes` and require `[Authorize]` (send `X-Mock-Role` in dev).

| Method & Route | Action | Notes |
|---|---|---|
| `POST /api/workflow-classes` | Create Draft | `{ name, version, definition: <WorkflowClassBlueprint> }`. `400` with `{ errors }` if invalid. |
| `PUT /api/workflow-classes/{id}` | Update Draft | Re-validates on every update. |
| `GET /api/workflow-classes` | List | Query filters: `?scope=Private\|Shared\|Public`, `?status=Draft\|Published\|...`. Returns own classes + all `Public` ones. |
| `GET /api/workflow-classes/{id}` | Get by id | `403 Forbidden` if `Private` and not owned by caller's tenant. |
| `POST /api/workflow-classes/{id}/publish` | Publish | Compiles to a runtime `WorkflowDefinition` and auto-publishes any new `EventDefinition`s referenced by the blueprint. |
| `POST /api/workflow-classes/{id}/submit` | Submit for review | Draft/Published → `Shared`. |
| `POST /api/workflow-classes/{id}/withdraw` | Withdraw submission | `Shared` → back to owner scope. |
| `POST /api/workflow-classes/{id}/validate` | Validate only | Returns `{ isValid, errors }` — always `200 OK`, even when invalid (the *validate action* succeeded). |
| `POST /api/workflow-classes/lint` | Advisory lint | `{ jsonContent }` → structural warnings (not authoritative). |
| `POST /api/workflow-classes/{id}/deprecate` | Deprecate | No new instances/copies afterward. |
| `POST /api/workflow-classes/{id}/abandon` | Abandon | Owner-initiated retirement. |
| `POST /api/workflow-classes/{id}/approve` | Approve as Public | Promotes `Shared` → `Public` (admin action; not currently gated by an explicit role check in code — see [Chapter 15](15-known-limitations-and-gaps.md)). |
| `POST /api/workflow-classes/{id}/copy` | Copy to tenant | `{ newTenantId }`. Only `Public` classes can be copied; `newTenantId` must equal the caller's own tenant. Resets to Draft, version reset. |
| `POST /api/workflow-classes/{id}/new-version` | New version | Auto-increments the minor version (`1.0.0` → `1.1.0`); links `PreviousVersionId`. |
| `DELETE /api/workflow-classes/{id}` | Delete | `400` if instances already exist for it. |

### Publish

```bash
curl -X POST "http://localhost:5183/api/workflow-classes/<id>/publish" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -H "X-Mock-Role: Admin"
```

### Copy a Public template into your tenant

```bash
curl -X POST "http://localhost:5183/api/workflow-classes/<id>/copy" \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: 22222222-2222-2222-2222-222222222222" \
  -H "X-Mock-Role: Admin" \
  -d '{ "newTenantId": "22222222-2222-2222-2222-222222222222" }'
```

## Where to go next

* [Chapter 13 — MCP & AI Agent Automation](13-mcp-and-ai-agent-integration.md) for how AI agents author `WorkflowClass` drafts through a governed, design-time-only tool surface.
* [Chapter 14 — API Reference](14-api-reference.md) for the full endpoint catalog including request/response DTOs.
