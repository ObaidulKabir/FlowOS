# 03 - Human Tasks

Workflows often pause for human input. In FlowOS, a "HumanTask" step waits for a completion signal.

## Scenario
The workflow is at **DesignTask**. A designer has finished their work and clicks "Complete" in your UI.

## Code Example
*Derived from: `tests/FlowOS.EndToEndTests/DesignConsultancy/DesignConsultancy_HappyPath.cs`*

```csharp
// POST /api/tasks/{workflowId}/complete
var response = await client.PostAsync($"/api/tasks/{workflowId}/complete", null);
response.EnsureSuccessStatusCode();
```

## What Happened?
1. **Signal**: You sent a signal that the task associated with this workflow instance is complete.
2. **Transition**: FlowOS received `TaskCompleted` event.
3. **Logic**: The definition says `DesignTask` -> `TaskCompleted` -> `Review`.
4. **State Change**: The workflow moved to **Review**.

## Verification
```csharp
var state = await client.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
// Assert.Equal("Review", state.CurrentStepId);
```
