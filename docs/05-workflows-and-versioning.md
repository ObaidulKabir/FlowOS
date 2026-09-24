# 5. Workflows & Versioning (The Work)

A Workflow Definition describes the sequence of steps and time-based orchestration for a process. Workflows handle **when** things happen; they must align with the State Machine, which decides **what** is legal (see [Chapter 3](03-state-machines.md)).

## Authoring: configuration or code — with no privilege difference

FlowOS supports creating workflows via **Configuration** (JSON) or **Code** (C# builder). Both are valid, and neither grants runtime authority:

* **Single runtime representation** — the engine doesn't know or care whether a `WorkflowDefinition` came from JSON or C#.
* **Publish gate** — every workflow must be explicitly `Published` before it can be started.
* **Same validation** — identical rules apply to both authoring paths.

### Option A — JSON configuration (`flowos-config/workflows/*.json`)

```json
{
  "name": "ExpenseApproval",
  "version": 1,
  "steps": [
    { "stepId": "Submit", "stepType": "Command", "nextSteps": { "Submitted": "ManagerReview" } },
    {
      "stepId": "ManagerReview",
      "stepType": "HumanTask",
      "allowedRoles": ["Manager"],
      "requiredCapabilities": ["event.publish.Approved", "event.publish.Rejected"],
      "nextSteps": { "Approved": "FinanceReview", "Rejected": "End" }
    },
    { "stepId": "FinanceReview", "stepType": "HumanTask", "allowedRoles": ["Finance"], "requiredCapabilities": ["event.publish.Paid"], "nextSteps": { "Paid": "End" } }
  ]
}
```

### Option B — C# builder (bootstrapping/tests)

```csharp
var definition = new WorkflowDefinition(tenantId, "ExpenseApproval", 1);

definition.AddStep(new WorkflowStepDefinition("Submit", WorkflowStepType.Command) {
    NextSteps = { { "Submitted", "ManagerReview" } }
});
definition.AddStep(new WorkflowStepDefinition("ManagerReview", WorkflowStepType.HumanTask) {
    AllowedRoles = { "Manager" },
    NextSteps = { { "Approved", "FinanceReview" }, { "Rejected", "End" } }
});

definition.Publish(); // Mandatory — required before any instance can start
```

Valid `WorkflowStepType` values (`FlowOS.Workflows.Enums.WorkflowStepType`): `Command`, `SystemTask`, `HumanTask`, `Timer`, `Decision`, `End`.

### Non-negotiable rules

1. No privileged creation path — code-created workflows cannot bypass `Publish()`.
2. You cannot "hot-patch" a running instance by changing the C# code or JSON file. You must publish a new version.

## Starting a workflow

**Endpoint:** `POST /api/workflows/start` → `StartWorkflowCommand`

You can start a workflow by **name** (recommended) or by **definition ID**:

```bash
curl -X POST "http://localhost:5183/api/workflows/start" \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -H "X-Mock-Role: Admin" \
  -d '{
    "tenantId": "11111111-1111-1111-1111-111111111111",
    "workflowName": "OrderApprovalWorkflow",
    "version": 1
  }'
```

```json
{ "workflowInstanceId": "<GUID>" }
```

Omit `initialStepId` to use the workflow's default start step, or pass it explicitly (e.g. to jump directly to `"ReviewStep"` for testing). Requires the `workflow.start` capability (see [Chapter 8](08-security-roles-and-policies.md)).

*Derived from: `tests/FlowOS.EndToEndTests/DesignConsultancy/DesignConsultancy_HappyPath.cs`, `docs`-verified against `StartWorkflowCommand` in `src/FlowOS.Application/Commands/WorkflowCommands.cs`.*

What happens, in order:

1. **Validation** — FlowOS checks the named workflow/version exists.
2. **Policy check** — verifies the caller has `workflow.start`.
3. **Resolution** — resolves the start step (explicit `initialStepId`, or the definition's default).
4. **Execution** — the instance is created at that step.
5. **Auto-advance** — if that step has a `"Default"` transition, the engine automatically advances past it.

## Checking status

```bash
# Basic status
curl -X GET "http://localhost:5183/api/workflows/<WORKFLOW_INSTANCE_ID>" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111"

# Detailed status + full audit timeline
curl -X GET "http://localhost:5183/api/admin/workflows/<WORKFLOW_INSTANCE_ID>" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
```

Listing all instances for a tenant (optionally filtered by status — `Running`, `Waiting`, `Completed`, `Failed`):

```bash
curl -X GET "http://localhost:5183/api/workflows?status=Running" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
```

## Advancing a workflow via an event

```bash
curl -X POST "http://localhost:5183/api/events/publish" \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -d '{
    "tenantId": "11111111-1111-1111-1111-111111111111",
    "workflowInstanceId": "<WORKFLOW_INSTANCE_ID>",
    "eventType": "EVT-ORDER-APPROVED"
  }'
```

Check `currentStepId` again via the Admin endpoint above to confirm the transition.

## Versioning & Safe Evolution

FlowOS supports semantic versioning (SemVer) for workflows: each published version is a distinct, immutable definition.

* **Deploying a new version** — create a new version via `POST /api/workflow-classes/{id}/new-version?bump=Major|Minor|Patch`, attach an optional `changeLog`, update the draft blueprint, and publish.
* **Starting a specific version** — include `"version": 1` in the start request.
* **Starting the latest version** — omit `version` entirely; FlowOS resolves and starts the highest available published version.
* **Verifying which version ran** — `GET /api/admin/workflows` returns the actual `version` for each running instance.

### Instance Pinning Guarantee
Once a workflow instance starts, **it is permanently pinned to its exact version definition**. An instance never silently migrates or mutates mid-flight, ensuring absolute audit integrity and deterministic state transitions.

### How to Change Operational Values (SLAs, Reminders, Thresholds)
Published definitions cannot be edited in place. When business rules or SLA policies change, FlowOS provides three distinct adaptation mechanisms:

1. **Patch Versioning (`1.0.0` → `1.0.1`) — Process-Wide Policy Shifts**:
   When standard SLA times or reminder schedules change (e.g. shortening manager approval SLA from 48h to 24h), create a **Patch version**:
   ```bash
   POST /api/workflow-classes/{id}/new-version?bump=Patch
   Content-Type: application/json
   { "changeLog": "Shortened ManagerReview SLA from 48h to 24h per Q4 operational guidelines" }
   ```
   * Existing in-flight instances finish under their original `1.0.0` agreement.
   * All new instances immediately execute the updated `1.0.1` SLA.

2. **Workflow Context Bindings — Tenant / Environment Parameterization**:
   To vary SLAs, roles, or approval limits across tenants or customer tiers without touching the workflow template, bind parameters in a **Workflow Context Binding Revision** (see [Chapter 17](17-workflow-context-bindings.md)). Updating a context binding revision updates runtime parameters without forking the template.

3. **Dynamic Payload Timers — Instance-Specific Deadlines**:
   Standalone `Timer` steps support dynamic schedules computed from instance payloads (e.g. `conditions: { targetTimestampProperty: "appointmentDate", leadTime: "-24h" }`). The deadline is calculated per-instance at runtime.

### Safe Rollback
If a newly published workflow version exhibits flaws, administrators can trigger a safe rollback:
```bash
POST /api/workflow-classes/{id}/rollback
```
* Safely marks the current version as `Deprecated` with a pointer to the previous version (`DeprecationMigrationTargetId`).
* **Running instances are never interrupted** — they complete on their pinned definition.
* New instance starts fall back to the prior stable published version.

```json
// Start latest
{ "workflowName": "OrderProcessing" }

// Start specific version
{ "workflowName": "OrderProcessing", "version": 1 }
```

## Troubleshooting

* **400 Bad Request** — check JSON syntax and that `workflowInstanceId` is correct.
* **Workflow not found** — confirm you're using the correct `x-tenant-id` header.
* **Event processing failed** — confirm the workflow is at a step that defines a transition for the event you're publishing (see [Chapter 4](04-events-and-registry.md#validation-logic-at-publish-time)).

## Where to go next

* [Chapter 6 — Human Tasks & Decisions](06-human-tasks-and-decisions.md) for branching logic.
* [Chapter 9 — WorkflowClass Governance](09-workflow-class-governance.md) for the higher-level authoring/versioning model that bundles events, state machine, workflow, and roles into one governed unit.
