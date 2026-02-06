# 06 - Policies and Capabilities

Governance is a first-class citizen. Policies can block actions even if the workflow logic allows them.

## Scenario
A global policy "Weekend Freeze" (simulated as "DenyAll") is active. A user attempts to start a workflow or publish an event.

## Code Example
*Derived from: `tests/FlowOS.EndToEndTests/DesignConsultancy/DesignConsultancy_PolicyBlock.cs`*

```csharp
var startCommand = new StartWorkflowCommand(tenantId, null, "DesignConsultancy", 1, Guid.Empty, "Start", Guid.NewGuid());
var response = await client.PostAsJsonAsync("/api/workflows/start", startCommand);

// Assert
if (!response.IsSuccessStatusCode)
{
    var content = await response.Content.ReadAsStringAsync();
    // Expected: "DenyAll policy is active"
}
```

## Guarantees
- **Safety**: No state changes occurred. The workflow did not start.
- **Feedback**: The API returned an error explaining *why* (Policy Violation).
- **Security**: Even admins are subject to Policies if configured.
