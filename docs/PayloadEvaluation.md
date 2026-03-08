# Payload Evaluation in Business Logic

This document describes how to use event payloads and decision steps to implement business logic in FlowOS workflows.

## Overview

FlowOS allows you to pass data (payloads) when publishing events. This data can be used by **Decision Steps** within a workflow definition to dynamically route the process based on business rules (e.g., expense amount thresholds, category-based routing).

## 1. Publishing Events with Payloads

When triggering a workflow transition, include a JSON payload with your event.

### C# Example
```csharp
var command = new PublishEventCommand
{
    TenantId = tenantId,
    WorkflowInstanceId = instanceId,
    EventType = "EVT-SUBMIT-EXPENSE",
    Payload = new Dictionary<string, object>
    {
        { "Amount", 500 },
        { "Category", "Hardware" },
        { "RiskScore", 85 }
    }
};
await _mediator.Send(command);
```

### HTTP API Example
```json
POST /api/events/publish
{
  "eventType": "EVT-SUBMIT-EXPENSE",
  "workflowInstanceId": "...",
  "payload": {
    "Amount": 1500,
    "CategoryCode": 1
  }
}
```

## 2. Defining Business Rules in Workflows

In your Workflow Definition (JSON), use the `Decision` step type. The `conditions` dictionary maps an expression to a target step ID.

### Supported Expressions
The engine currently supports simple binary comparisons:
*   `Key > Value`
*   `Key < Value`
*   `Key >= Value`
*   `Key <= Value`
*   `Key == Value`

### Example Definition

```json
{
  "steps": [
    {
      "stepId": "Start",
      "stepType": "SystemTask",
      "nextSteps": {
        "EVT-SUBMIT-EXPENSE": "CheckAmount"
      }
    },
    {
      "stepId": "CheckAmount",
      "stepType": "Decision",
      "conditions": {
        "Amount > 1000": "DirectorApproval",
        "Amount <= 50": "AutoApproved",
        "CategoryCode == 1": "ITQueue"
      },
      "nextSteps": {
        "Default": "ManagerApproval"
      }
    },
    {
      "stepId": "DirectorApproval",
      "stepType": "HumanTask"
    },
    {
      "stepId": "AutoApproved",
      "stepType": "SystemTask"
    },
    {
      "stepId": "ManagerApproval",
      "stepType": "HumanTask"
    }
  ]
}
```

## 3. Tested Business Scenarios

We have verified the following 5 business scenarios via unit tests (`FlowOS.UnitTests/Engine/PayloadEvaluationTests.cs`):

| Scenario | Condition | Input Payload | Result |
| :--- | :--- | :--- | :--- |
| **High Value Expense** | `Amount > 1000` | `{ "Amount": 1500 }` | Routes to `DirectorApproval` |
| **Auto-Approval** | `Amount <= 50` | `{ "Amount": 45.50 }` | Routes to `AutoApproved` |
| **Category Routing** | `CategoryCode == 1` | `{ "CategoryCode": 1 }` | Routes to `ITQueue` |
| **Risk Assessment** | `RiskScore >= 80` | `{ "RiskScore": 80 }` | Routes to `AuditTeam` |
| **Fallback Logic** | (No match) | `{ "Amount": 500 }` | Routes to `FallbackStep` (via Default) |

## 4. Implementation Details

*   **Ingestion**: Payloads are serialized to JSON and stored in the `Events` table metadata.
*   **Evaluation**: The `WorkflowEngine` deserializes the payload into a dictionary and evaluates expressions using a helper method (to be replaced by CEL or Dynamic LINQ in future phases).
*   **Fallback**: If no condition is met, the engine looks for a `"Default"` key in the `conditions` or `nextSteps` to determine the fallback path.
