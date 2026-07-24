# 11. Recovery & Resilience

FlowOS is designed to be resilient. If the hosting process crashes, workflow state is persisted safely in the database — nothing lives only in memory.

## Scenario: the API process crashes mid-workflow

*Derived from: `tests/FlowOS.EndToEndTests/Recovery/WorkflowResumeAfterCrash.cs`*

**Before the crash:**

1. A workflow is started.
2. It reaches state `DesignTask`.
3. The process is terminated.

**After restart**, a brand-new client/process connects to the same database and simply reads the state back:

```csharp
var state = await newClient.GetFromJsonAsync<WorkflowStateDto>($"/api/workflows/{workflowId}");
Assert.Equal("DesignTask", state.CurrentStepId); // Nothing was lost
```

**Resuming** is just... continuing normally:

```csharp
await newClient.PostAsync($"/api/tasks/{workflowId}/complete", null);
```

## Why this works

* **Persistence** — every state transition is transactional. `EventPublishingInterceptor` (an EF Core `SaveChangesInterceptor`) ensures the domain event and the state mutation commit together, atomically.
* **Event-sourced derivation** — current state (`WorkflowInstance.CurrentStepId`, `Status`) is always derivable by replaying the Event Log, so a restart never has to "guess" where a workflow was.
* **Consistency** — the system never enters an invalid state as a side effect of a crash; a half-applied transition simply never committed in the first place.
* **Idempotency** — processing the same inbound message twice (e.g. a retried HTTP request) produces the same result, avoiding duplicate side effects from client-side retry logic.

## Where to go next

* [Chapter 10 — Notifications](10-notifications.md) for the analogous (but weaker — "best effort") guarantee that applies to the *notification* side effect, as opposed to the core business transaction described here.
* [Chapter 12 — Anti-Patterns](12-anti-patterns.md) for client-side mistakes that undermine these guarantees.
