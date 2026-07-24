# 7. AI Agents & Insights

In FlowOS, agents act as intelligent advisors, **never** autonomous executors. They analyze workflow state and business data, then produce **Insights** and **Suggested Actions**, which a human (or a validated policy) must confirm before anything actually changes.

## Agent contract

1. **Input**: `AgentContext` — a read-only snapshot of the tenant, entity, and workflow state.
2. **Output**: `AgentResult` — an insight string, optional structured data, and optional `SuggestedAction`s.
3. **Side effects**: none. An agent cannot call `PublishEventCommand` or `StartWorkflowCommand` directly.

```csharp
public interface IWorkflowAgent
{
    Task<AgentResult> ExecuteAsync(AgentContext context);
}
```

## Publishing a plain insight

**Endpoint:** `POST /api/agents/insight`

```bash
curl -X POST "http://localhost:5183/api/agents/insight" \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -d '{
    "workflowInstanceId": "<WORKFLOW_INSTANCE_ID>",
    "agentId": "Risk-Analyzer-Bot-01",
    "insight": "Transaction risk score is 85/100. Manual review recommended.",
    "contextObjective": "Risk Assessment"
  }'
```

```json
{ "success": true, "message": "Agent insight recorded." }
```

Insights are recorded as `AgentInsightGenerated` events, projected into the workflow's timeline, and surfaced on the associated task's `agentInsights` list (`GET /api/tasks/{id}`). They **do not** trigger state transitions.

## Suggested Actions — going beyond insight

Beyond insights, agents can propose **Suggested Actions**: a specific event, a human-readable reason, and a confidence score.

```csharp
public class RiskAnalysisAgent : IWorkflowAgent
{
    public Task<AgentResult> ExecuteAsync(AgentContext context)
    {
        var expense = context.EntitySnapshot as Dictionary<string, object>;
        if ((double)expense["Amount"] > 5000)
        {
            return Task.FromResult(AgentResult.WithActions("High Risk Detected", new List<SuggestedAction> {
                new SuggestedAction("EVT-ESCALATE", "Amount > $5k", 0.95)
            }));
        }
        return Task.FromResult(AgentResult.FromInsight("No risk detected"));
    }
}
```

* **Correct**: "Agent suggests Escalation (95% confidence)." A human reads this and clicks "Confirm", which publishes the actual `EVT-ESCALATE` event through the normal API.
* **Incorrect** (impossible in FlowOS): the agent directly calling something equivalent to `Approve()`.

These suggestions appear in the UI as **Smart Actions** — buttons the user must explicitly click to execute.

## Business use cases

| Agent Type | Trigger | Logic | Suggestion |
|---|---|---|---|
| Risk Analyzer | Expense submission | Checks amount, history, category against fraud patterns | `EVT-ESCALATE` or `EVT-FLAG-FRAUD` |
| Auto-Approver | Low-value request | Verifies budget/policy compliance | `EVT-APPROVE` |
| Compliance Bot | Contract review | Scans document for missing clauses | `EVT-REQUEST-CHANGES` |
| Router | Support ticket | Classifies sentiment/topic | `EVT-ROUTE-TIER2` |

## Governance guarantees

* **Read-only context** — agents receive a snapshot; they cannot cause side effects during reasoning.
* **Explicit intent** — agents output structured events, never free-form commands.
* **State Machine enforcement still applies** — even if an agent suggests an action and a human confirms it, the `WorkflowEngine` validates the resulting event against defined transitions exactly as it would for a human-originated event. An agent's suggestion carries no special authority.

## Verification: seeing human + AI events merged in one timeline

```bash
curl -X GET "http://localhost:5183/api/admin/workflows/<WORKFLOW_INSTANCE_ID>" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
```

```json
{
  "timeline": [
    { "eventType": "AgentInsightGenerated", "summary": "Agent Risk-Analyzer-Bot-01 suggested: Transaction risk score is 85/100...", "keyData": { "Agent": "Risk-Analyzer-Bot-01", "Objective": "Risk Assessment" } },
    { "eventType": "TaskCompleted", "summary": "Task completed by user", "keyData": { "TaskId": "...", "UserId": "..." } }
  ]
}
```

## For automated/design-time agent integration via MCP

The patterns above are for agents integrated **into a running workflow instance** (advisory, runtime). If you're building an AI tool that *designs* workflows rather than participating in them, see [Chapter 13 — MCP & AI Agent Automation](13-mcp-and-ai-agent-integration.md) — a completely separate, design-time-only surface with its own strict governance boundaries.

## Where to go next

* [Chapter 13 — MCP & AI Agent Automation](13-mcp-and-ai-agent-integration.md) for the design-time governance model.
* [Chapter 15 — Known Limitations](15-known-limitations-and-gaps.md) for what's still a demo/simulation in the current agent tooling.
