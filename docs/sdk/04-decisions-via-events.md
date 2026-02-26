# 04 - Decisions via Events

Sometimes the next step isn't just "next". It's a choice. FlowOS handles branching via custom events.

## Scenario
The workflow is at **Review**. The manager needs to either **Approve** or **Reject**.

### Path A: Approval
*Derived from: `tests/FlowOS.EndToEndTests/DesignConsultancy/DesignConsultancy_HappyPath.cs`*

```csharp
var approveEvent = new PublishEventCommand(
    tenantId,
    workflowId,
    "EVT-DESIGN-APPROVED",
    Guid.NewGuid()
);

var response = await client.PostAsJsonAsync("/api/events/publish", approveEvent);
```

**Outcome**: Workflow transitions to **END** (Completed).

### Path B: Rejection
*Derived from: `tests/FlowOS.EndToEndTests/DesignConsultancy/DesignConsultancy_Rejection.cs`*

```csharp
var rejectEvent = new PublishEventCommand(
    tenantId,
    workflowId,
    "EVT-DESIGN-REJECTED",
    Guid.NewGuid()
);

var response = await client.PostAsJsonAsync("/api/events/publish", rejectEvent);
```

**Outcome**: Workflow transitions to **Rejected**, which then auto-advances to **END**.

## Important Note
You did not tell FlowOS "Go to End" or "Go to Rejected". You only said "Design Approved" or "Design Rejected". FlowOS decided the destination.

## Decisions via Rules (Decision Step)
Sometimes, you want the **Workflow Engine** to make the decision based on data, rather than waiting for an external event.

### Scenario
If the **Amount > 100**, go to **Director Approval**. Otherwise, go to **Manager Approval**.

### Implementation (JSON)
Use the `Decision` step type with `Conditions`.

```json
{
  "stepId": "CheckAmount",
  "stepType": "Decision",
  "conditions": {
    "Amount > 100": "DirectorApproval",
    "Amount <= 100": "ManagerApproval"
  }
}
```

The engine evaluates these rules against the event payload and automatically routes the workflow.
