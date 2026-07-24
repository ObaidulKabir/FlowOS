# 6. Human Tasks & Decisions

## Human tasks

Workflows often pause for human input. A `HumanTask` step explicitly pauses execution (the instance's status becomes `Waiting`) until a user with a required role performs an action.

**Scenario:** the workflow is at `DesignTask`. A designer finishes their work and clicks "Complete" in the UI.

*Derived from: `tests/FlowOS.EndToEndTests/DesignConsultancy/DesignConsultancy_HappyPath.cs`*

```bash
curl -X POST "http://localhost:5183/api/tasks/<WORKFLOW_INSTANCE_ID>/complete" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -H "X-User-ID: user-123"
```

What happened:

1. **Signal** — you told FlowOS the task associated with this workflow instance is complete.
2. **Transition** — FlowOS received a generic `TaskCompleted` event.
3. **Logic** — the definition maps `DesignTask` --(`TaskCompleted`)--> `Review`.
4. **State change** — the workflow moved to `Review`.

> **Note:** `POST /api/tasks/{id}/complete` always emits a generic `TaskCompleted` event and is best for linear steps where completion simply moves to the next step. For **Decision steps** (Approve vs. Reject), publish the specific outcome event explicitly via `/api/events/publish` instead — see below.

Verify:

```bash
curl -X GET "http://localhost:5183/api/workflows/<WORKFLOW_INSTANCE_ID>" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
# currentStepId should now be "Review"
```

### Listing and reading tasks

```bash
curl -X GET "http://localhost:5183/api/tasks" -H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
curl -X GET "http://localhost:5183/api/tasks/<id>" -H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
```

Each `TaskDto` includes `taskId`, `workflowId`, `currentStep`, `requiredRole`, `status`, and any `agentInsights` recorded for that instance (see [Chapter 7](07-ai-agents-and-insights.md)).

## Decisions via explicit events

Sometimes the next step isn't just "next" — it's a choice. FlowOS handles branching via distinct events for each outcome.

**Scenario:** the workflow is at `Review`. The manager must either Approve or Reject.

*Derived from: `tests/FlowOS.EndToEndTests/DesignConsultancy/DesignConsultancy_HappyPath.cs` and `DesignConsultancy_Rejection.cs`*

```csharp
// Path A: Approval → workflow transitions to END (Completed)
var approveEvent = new PublishEventCommand(tenantId, workflowId, "EVT-DESIGN-APPROVED", Guid.NewGuid());
await client.PostAsJsonAsync("/api/events/publish", approveEvent);

// Path B: Rejection → workflow transitions to "Rejected", which auto-advances to END
var rejectEvent = new PublishEventCommand(tenantId, workflowId, "EVT-DESIGN-REJECTED", Guid.NewGuid());
await client.PostAsJsonAsync("/api/events/publish", rejectEvent);
```

You never told FlowOS "go to End" or "go to Rejected" — you only said "Design Approved" or "Design Rejected". FlowOS decided the destination based on the workflow definition.

## Decisions via data (the `Decision` step)

Sometimes you want the **engine itself** to decide, based on payload data, rather than waiting for an external outcome event.

**Scenario:** if `Amount > 1000`, route to `DirectorApproval`; otherwise `ManagerApproval`.

```json
{
  "stepId": "CheckAmount",
  "stepType": "Decision",
  "conditions": {
    "Amount > 1000": "DirectorApproval",
    "Amount <= 50": "AutoApproved",
    "CategoryCode == 1": "ITQueue"
  },
  "nextSteps": { "Default": "ManagerApproval" }
}
```

### Supported expressions

The engine currently supports simple binary comparisons evaluated (via `DynamicExpresso`) against the event payload:

* `Key > Value`
* `Key < Value`
* `Key >= Value`
* `Key <= Value`
* `Key == Value`

If no condition matches, the engine follows the `"Default"` key in `nextSteps` (or `conditions`, depending on how the step is authored).

### Publishing an event with a payload to drive a decision

```bash
curl -X POST "http://localhost:5183/api/events/publish" \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -d '{
    "tenantId": "11111111-1111-1111-1111-111111111111",
    "workflowInstanceId": "<WORKFLOW_INSTANCE_ID>",
    "eventType": "EVT-SUBMIT-EXPENSE",
    "payload": { "Amount": 1500, "CategoryCode": 1 }
  }'
```

C# equivalent:

```csharp
var command = new PublishEventCommand(
    TenantId: tenantId,
    WorkflowInstanceId: instanceId,
    EventType: "EVT-SUBMIT-EXPENSE",
    Payload: new Dictionary<string, object> {
        { "Amount", 500 }, { "Category", "Hardware" }, { "RiskScore", 85 }
    }
);
await _mediator.Send(command);
```

### Verified business scenarios

These scenarios are covered by unit tests (`FlowOS.UnitTests`, payload-evaluation engine tests):

| Scenario | Condition | Input Payload | Result |
|---|---|---|---|
| High-value expense | `Amount > 1000` | `{ "Amount": 1500 }` | Routes to `DirectorApproval` |
| Auto-approval | `Amount <= 50` | `{ "Amount": 45.50 }` | Routes to `AutoApproved` |
| Category routing | `CategoryCode == 1` | `{ "CategoryCode": 1 }` | Routes to `ITQueue` |
| Risk assessment | `RiskScore >= 80` | `{ "RiskScore": 80 }` | Routes to `AuditTeam` |
| Fallback logic | (no match) | `{ "Amount": 500 }` | Routes to `FallbackStep` via `Default` |

### Implementation details

* **Ingestion** — payloads are serialized to JSON and stored on the persisted `Event`'s metadata.
* **Evaluation** — `WorkflowEngine` deserializes the payload into a dictionary and evaluates each condition expression via `DynamicExpresso`.
* **Fallback** — if no condition matches, the engine looks for a `"Default"` key to determine the fallback path.

## Where to go next

* [Chapter 7 — AI Agents & Insights](07-ai-agents-and-insights.md) — agents can *suggest* the events described above, but a human still confirms them.
* [Chapter 9 — WorkflowClass Governance](09-workflow-class-governance.md#validation-rules-strict) for the strict validation rules `Decision` and `HumanTask` steps must satisfy when authored via a `WorkflowClass` blueprint.
