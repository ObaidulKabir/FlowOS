# 07 - Recovery and Retries

FlowOS is designed to be resilient. If the hosting process crashes, the workflow state is persisted safely in the database.

## Scenario
The workflow engine (or API server) crashes while a workflow is waiting at **DesignTask**.

## Code Example
*Derived from: `tests/FlowOS.EndToEndTests/Recovery/WorkflowResumeAfterCrash.cs`*

1. **Before Crash**:
   - Workflow started.
   - State: `DesignTask`.
   - *Process Terminated*.

2. **After Restart**:
   ```csharp
   // New Client/Process connects to the same database
   var state = await newClient.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
   
   // Assert: State is still "DesignTask"
   Assert.Equal("DesignTask", state.CurrentStepId);
   ```

3. **Resuming**:
   You can simply continue where you left off.
   ```csharp
   await newClient.PostAsync($"/api/tasks/{workflowId}/complete", null);
   ```

## Guarantees
- **Persistence**: Every state transition is transactional.
- **Consistency**: The system never enters an invalid state due to a crash.
