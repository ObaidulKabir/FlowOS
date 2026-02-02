# 09 - Notifications

FlowOS automatically projects Domain Events into user-facing Notifications. This decouples the "Workflow Engine" (State) from the "User Interface" (Awareness).

## The Projection Model
1. **Event Occurs**: A workflow step completes, or you publish an event (e.g., `EVT-DESIGN-APPROVED`).
2. **Commit**: The state change is saved to the database.
3. **Project**: *After* the commit, FlowOS translates the event into a `Notification` record.
4. **Broadcast**: The notification is pushed to connected clients (e.g., via SSE/SignalR).

## Scenario
You want to verify that an event triggers a notification.

## Code Example
*Derived from: `tests/FlowOS.EndToEndTests/Notifications/Notification_HappyPath.cs`*

```csharp
// 1. Publish an Event
var approveEvent = new PublishEventCommand(
    tenantId,
    workflowId,
    "EVT-DESIGN-APPROVED",
    null // Defaults to WorkflowInstanceId
);

await client.PostAsJsonAsync("/api/events/publish", approveEvent);

// 2. Consume Notification (e.g., via API polling or Stream)
// The system automatically created a Notification record linked to this event.
```

## Failure Isolation
**"Notifications do not break Workflows."**

Because notifications are projected *after* the database transaction commits, a failure in the notification subsystem (e.g., Email Service down, SignalR error) **will not** roll back the workflow state.

*Derived from: `tests/FlowOS.EndToEndTests/Notifications/Notification_FailureIsolation.cs`*

> If a notification handler throws an exception, the Event is **still persisted**, and the Workflow **still advances**. The API may report a warning or error, but the business transaction is safe.

## Idempotency
**"State is Authoritative. Notifications are Informational."**

If an event is processed multiple times (e.g., due to network retries), FlowOS ensures the **Workflow State** remains consistent. However, you might receive duplicate notifications.

*Derived from: `tests/FlowOS.EndToEndTests/Notifications/Notification_Idempotency.cs`*

> If you publish `EVT-DUP` twice, the workflow stays "Completed". You may see two notifications in the user's feed, which is an acceptable trade-off for system robustness.
