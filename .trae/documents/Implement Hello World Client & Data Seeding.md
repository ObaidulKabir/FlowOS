I will implement a "Hello World" client and seed the necessary data to make it work.

### 1. Seed Data (FlowOS.API)
Since the system runs on an In-Memory Database, it starts empty. I will add a `Seeder` to `Program.cs` that initializes:
*   **Tenant**: `11111111-1111-1111-1111-111111111111`
*   **Workflow Definition**: "HelloWorld" (Id: `22222222-2222-2222-2222-222222222222`)
    *   Step `Start` -> Transitions to `End` on event `Complete`.

### 2. Create Client Application (FlowOS.Client)
I will create a new Console Application in `src/FlowOS.Client` to act as the test driver.
*   **Action**: It will send a `POST` request to `http://localhost:5005/api/workflows/start`.
*   **Payload**:
    ```json
    {
      "TenantId": "11111111-1111-1111-1111-111111111111",
      "WorkflowDefinitionId": "22222222-2222-2222-2222-222222222222",
      "Version": 1,
      "InitialStepId": "Start"
    }
    ```
*   **Output**: It will print the new `WorkflowInstanceId`.

### 3. Execution
1.  Stop the running API.
2.  Apply the Seeder changes.
3.  Restart the API.
4.  Run the Client to verify the "Hello World" flow.