# 10 - Creating Workflows

FlowOS supports creating workflows via **Configuration** (JSON) or **Code** (C# Builder). Both are valid authoring mechanisms. Neither grants runtime authority.

## The Principle of Parity
Regardless of how a workflow is defined, it compiles down to the same immutable `WorkflowDefinition` model in the kernel.
*   **No Hierarchy**: Code-defined workflows have no extra privileges.
*   **Publish Gate**: All workflows must be explicitly `Published` before they can be started.
*   **Validation**: The same validation rules apply to both.

## Example: The "Parity" Workflow
Here is the same workflow defined in two ways. Both produce identical runtime behavior.

### Option A: Configuration (JSON)
Recommended for separation of concerns and non-developer authoring.

```json
{
  "name": "ParityWorkflow",
  "version": 1,
  "steps": [
    {
      "stepId": "Start",
      "stepType": "Command",
      "nextSteps": {
        "Default": "CheckAmount"
      }
    },
    {
      "stepId": "CheckAmount",
      "stepType": "Decision",
      "conditions": {
        "Amount > 100": "End"
      }
    },
    {
      "stepId": "End",
      "stepType": "Command"
    }
  ]
}
```

### Option B: Code (C#)
Recommended for dynamic generation or unit testing.

```csharp
var definition = new WorkflowDefinition(tenantId, "ParityWorkflow", 1);

definition.AddStep(new WorkflowStepDefinition("Start", WorkflowStepType.Command) 
{ 
    NextSteps = { { "Default", "CheckAmount" } } 
});

var decision = new WorkflowStepDefinition("CheckAmount", WorkflowStepType.Decision);
decision.Conditions.Add("Amount > 100", "End");
definition.AddStep(decision);

definition.AddStep(new WorkflowStepDefinition("End", WorkflowStepType.Command));

// Mandatory: Must call Publish() to make it executable
definition.Publish(); 
```

## Non-Negotiable Rules

1.  **Single Runtime Representation**: The engine does not know if a workflow came from JSON or C#. It only sees `WorkflowDefinition`.
2.  **No Privileged Creation**: Code-created workflows cannot bypass the `Publish()` lifecycle.
3.  **Versioning**: You cannot "hot-patch" a running instance by changing the C# code. You must publish `v2`.
