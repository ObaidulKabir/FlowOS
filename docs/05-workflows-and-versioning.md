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
      "nextSteps": { "Approved": "FinanceReview", "Rejected": "End" }
    },
    { "stepId": "FinanceReview", "stepType": "HumanTask", "allowedRoles": ["Finance"], "nextSteps": { "Paid": "End" } }
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

## Versioning

FlowOS supports semantic versioning for workflows: each version is a distinct, immutable definition.

* **Deploying a new version** — create a new JSON file with an incremented `version` field, then load it via `POST /api/admin/config/publish`.
* **Starting a specific version** — include `"version": 1` in the start request.
* **Starting the latest version** — omit `version` entirely; FlowOS resolves and starts the highest available version number.
* **Verifying which version ran** — `GET /api/admin/workflows` returns the actual `version` for each running instance; an instance never silently migrates to a newer version once started (see [Chapter 2](02-core-concepts.md#versioning--immutability)).

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
