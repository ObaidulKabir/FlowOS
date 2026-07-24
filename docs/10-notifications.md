# 10. Notifications

FlowOS automatically projects Domain Events into user-facing Notifications. This decouples the Workflow Engine (state) from the UI (awareness): notifications are an **out-of-band** projection of the Event Log, not a source of truth.

## Philosophy

* **State is authoritative** — the Workflow Engine / database is the single source of truth.
* **Notifications are informational** — a derived view designed for human attention.
* **Isolation** — a failure to deliver a notification (e.g. email server down) **must not** roll back the core business transaction.

## Architecture

```
Workflow Engine ──publishes──▶ Domain Event ──intercepted by──▶ Event Interceptor
                                                                       │
                                                          projects to │
                                                                       ▼
                                                          Notification Projector
                                                          ├─▶ 1. Saves to Database
                                                          └─▶ 2. Pushes to Stream Service ──SSE──▶ Web Dashboard
```

| Component | Role | Location |
|---|---|---|
| `Notification` | Domain entity representing the alert | `src/FlowOS.Notifications/Domain/Notification.cs` |
| `NotificationProjector` | Maps Events → Notifications | `src/FlowOS.Notifications/Application/NotificationProjector.cs` |
| `NotificationStreamService` | Manages SSE connections and broadcasting | `src/FlowOS.Notifications/Application/NotificationStreamService.cs` |
| `NotificationsController` | API surface (history + stream) | `src/FlowOS.Notifications/Api/NotificationsController.cs` |
| `EventPublishingInterceptor` | EF Core `SaveChangesInterceptor` that fires post-commit | `src/FlowOS.Infrastructure/...` |

## Data model

| Property | Type | Description |
|---|---|---|
| `Id` | Guid | Unique id |
| `TenantId` | Guid | Multi-tenant isolation |
| `CorrelationId` | Guid? | Links to the source `WorkflowInstanceId`/`TaskId` |
| `EventType` | string | Technical event code (e.g. `EVT-TASK-ASSIGNED`) |
| `Message` | string | Human-readable alert text |
| `Severity` | string | `Info`, `Warning`, or `Critical` |
| `CreatedAt` | DateTime | UTC timestamp |

## Trying it out end-to-end

*Verified live against the API using an in-memory database.*

```bash
dotnet run --project src/FlowOS.Api/FlowOS.Api.csproj --urls=http://localhost:5183 --UseInMemoryDatabase=true
```

**1. No notifications yet:**

```bash
curl -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" -H "X-Mock-Role: Admin" \
  http://localhost:5183/api/notifications
# []
```

**2. Start a workflow:**

```bash
curl -X POST -H "Content-Type: application/json" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" -H "X-Mock-Role: Admin" \
  -d '{ "workflowName": "OrderProcessing", "version": 1 }' \
  http://localhost:5183/api/workflows/start
# { "workflowInstanceId": "..." }
```

**3. Publish an event that advances it:**

```bash
curl -X POST -H "Content-Type: application/json" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" -H "X-Mock-Role: Admin" \
  -d '{ "workflowInstanceId": "...", "eventType": "EVT-ORDER-APPROVED" }' \
  http://localhost:5183/api/events/publish
# "Event published"
```

**4. The notification is now there:**

```bash
curl -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" -H "X-Mock-Role: Admin" \
  http://localhost:5183/api/notifications
```

```json
[
  {
    "message": "Event: EVT-ORDER-APPROVED",
    "severity": "Info",
    "createdAt": "2026-01-31T13:05:38.3751232Z",
    "eventType": "EVT-ORDER-APPROVED"
  }
]
```

## Real-time stream (SSE)

**Endpoint:** `GET /api/notifications/stream`

```javascript
const eventSource = new EventSource('/api/notifications/stream');
eventSource.onmessage = (event) => {
  const notification = JSON.parse(event.data);
  toast.show(notification.Message);
};
```

Response headers set by the controller: `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive`. The connection is held open with a 1-second heartbeat loop until the client disconnects.

## Reliability guarantees (verified by E2E tests)

### Failure isolation — *"Notifications do not break Workflows."*

*Derived from: `tests/FlowOS.EndToEndTests/Notifications/Notification_FailureIsolation.cs`*

Because notifications are projected **after** the database transaction commits, a failure in the notification subsystem (e.g. a broken projector mapping, a stream error) does **not** roll back the workflow. The Event is still persisted and the Workflow still advances — at worst you get a logged warning, never a failed business transaction.

### Idempotency — *"State is Authoritative. Notifications are Informational."*

*Derived from: `tests/FlowOS.EndToEndTests/Notifications/Notification_Idempotency.cs`*

If an event is processed twice (e.g. a network retry), the **workflow state** stays consistent (e.g. remains `Completed`), but you may see two notifications in the user's feed. This is an intentional at-least-once delivery trade-off in favor of robustness over strict exactly-once complexity.

## Extending: adding a new notification type

1. **Define the event** — ensure your workflow publishes the new event code (e.g. `EVT-FRAUD-DETECTED`).
2. **Update the mapper** — edit `NotificationProjector.cs`:

   ```csharp
   "EVT-FRAUD-DETECTED" => new Notification(..., "Fraud check failed!", "Critical", ...),
   ```

3. **Deploy** — the system starts projecting the new event immediately; no other change is needed.

## Where to go next

* [Chapter 11 — Recovery & Resilience](11-recovery-and-resilience.md) for how this pattern extends to crash safety generally.
* [Chapter 16 — Sample Applications](16-sample-applications.md) to see the Tenant Dashboard consuming this stream.
