# 02 - Starting a Workflow

In this step, you start a new instance of a workflow. You do not define the first step; FlowOS determines it based on the definition.

## Scenario

You want to begin the "DesignConsultancy" process.

## Code Example

*Derived from: `tests/FlowOS.EndToEndTests/DesignConsultancy/DesignConsultancy_HappyPath.cs`*

```csharp
var startCommand = new StartWorkflowCommand(
    tenantId, 
    null, // Let FlowOS find definition by name
    "DesignConsultancy", 
    1, // Version
    Guid.Empty, // WorkflowClassId
    null, // OPTIONAL: Initial Step ID (null = use definition default)
    Guid.NewGuid() // Correlation ID
);

var response = await client.PostAsJsonAsync("/api/workflows/start", startCommand);
var result = await response.Content.ReadFromJsonAsync<WorkflowStartResponse>();
var workflowId = result.WorkflowInstanceId;
```

## What Happened?

1. **Validation**: FlowOS checked if "DesignConsultancy" v1 exists.
2. **Policy Check**: FlowOS verified if you have `workflow.start` permission.
3. **Resolution**: FlowOS resolved the start step.
   - If `initialStepId` was null, it used the definition's default (e.g., "Start").
   - If provided, it validated that the step exists in the definition.
4. **Execution**: The workflow started.
5. **Auto-Advance**: Since "Start" had a default transition, it automatically moved to "DesignTask".

## Verification

You can check the state immediately:

```csharp
var state = await client.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
// Assert.Equal("DesignTask", state.CurrentStepId);
```
