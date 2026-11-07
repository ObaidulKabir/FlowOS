# 7. AI Agents & Insights

In FlowOS, agents **always suggest**. FlowOS **auto-commits** a `SuggestedAction` only when an explicit step `autoCommit` policy says the case is in-bounds. Otherwise the instance stays a HumanTask and the suggestion is parked as a Smart Action. Dual-kernel law does not change: the state machine decides what is legal; the workflow graph decides where the instance sits. AI must not invent transitions.

## Bounded autonomy

Per waiting step:

* `actor`: `Human` (default, no behavior change) | `Agent` | `Either`
* `decisionGuideline`: template markdown — how to decide
* Binding `policyGuideline` + canonical context — the case and tenant overlay
* `autoCommit.minConfidence` and `autoCommit.allowedEvents` (subset of `nextSteps`)
* `agentPrompt`: alias of a tenant plugin binding with `bindingType: prompt` (create/edit `title`, `system`, `instructions`)
* `agentProvider`: alias of a tenant plugin binding with `bindingType: agent` (provider, model, endpoint, API key)
* `agentTools`: resource plugins (`LookupRecord:<capability>`, …), notify plugins, and `capability:*` writes; legal `nextSteps` events are always included. Read tools are prefetched into Agent Context.

When an `Agent`/`Either` waiting step becomes current, FlowOS builds a **DecisionPacket** (Prompt + Data + Tools + redacted Provider), runs `IWorkflowAgent`, records `AgentInsightGenerated`, then either calls the same `PublishEventCommand` path humans use (`ActorId` = `Agent:{agentId}`) or parks the insight. TimeoutEvent stays SLA/timer-owned. External MCP chat agents must not free-form `publish_event`; they call `get_agent_context` / `preview_agent_context` (inspect), `suggest_agent_action` (run agent, no publish), or `run_agent_task` (hosted loop). Design-time prompt: `design_agent_handled_step`. Resource: `flowos://guides/bounded-autonomy-tasks`.

## Consequential steps and the tenant switch (CR-11)

Some decisions must always be made by a person, for example certifying a learner or changing a grade.

* **`consequential: true` on a step.** An agent may still suggest an event there, but FlowOS never auto-commits it, whatever the step's `autoCommit`, the confidence or an agent profile's `autoCommitThreshold` say. The suggestion is parked for a person, and the execution record's `parkReason` says why. Configuring `autoCommit` on a consequential step fails validation with `WF-AGENT-006`. Simulation shows the same block.
* **Tenant switch.** `configure_agent_auto_commit` (MCP, Admin only) turns agent auto-commit off for the whole tenant; every suggestion is then parked. Turning it back on needs `confirmHumanApproval: true`. `get_external_agent_settings` shows the current value (`agentAutoCommitEnabled`). Existing tenants keep it on.
* The block is checked first in `AutoCommitEvaluator.CanCommit`, so nothing else can override it.

## Tenant-owned model and declarative tools

Paid tenants default to **FlowOS hosted OpenAI** (`flowos-hosted`) — see [Chapter 20](20-hosted-llm-and-automation-policy.md). BYO is optional. Register a tenant model with MCP `register_plugin_binding` or `upsert_agent_provider`:

```json
{
  "bindingType": "agent",
  "sourceName": "quote-llm",
  "providerName": "openai",
  "configuration": { "model": "gpt-4o-mini", "endpoint": "https://api.openai.com/v1", "apiKey": "<tenant-key>" }
}
```

Known `providerName` values: `flowos-hosted` (platform OpenAI, no tenant key), `openai`, `anthropic`, `azure-openai`, `google`, `custom`, `flowos-risk`. BYO API keys are write-only: omit on update to keep the stored secret; list/resolve return `{model,endpoint,hasApiKey,apiKeyFingerprint,keyUpdatedAtUtc}` and never the key - the fingerprint is non-reversible and only changes when the key itself changes. Step `agentProvider` is only the alias. Keys are never copied into DecisionPacket / Agent Context. Providers saved before these two fields existed show both as `null`; call `backfill_agent_provider_key_metadata` to compute them from the already-stored key without ever exposing or changing it (safe to call repeatedly; already-stamped and keyless providers are left alone, and a key that can no longer be decrypted is skipped - re-enter it, see chapter 8).

The alias `flowos-hosted` is platform-reserved: `upsert_agent_provider` / `register_plugin_binding` reject (`AGENT-ALIAS-RESERVED`) saving any non-`flowos-hosted` `providerName` under that exact alias, since a step whose `agentProvider` resolves to it always gets the platform's hosted lease at runtime regardless of what was saved. `delete_agent_provider` permanently removes a provider binding (`confirmHumanApproval:true` required) - use `isEnabled:false` instead to only disable one.

Writing, deleting, or backfilling an agent provider - via `upsert_agent_provider`, `delete_agent_provider`, `backfill_agent_provider_key_metadata`, `register_plugin_binding` (bindingType `agent`), or REST `PUT /api/plugin-bindings` (bindingType `agent`) - requires the `agent.manage` capability (see [Chapter 8](08-security-roles-and-policies.md#recommended-capability-codes)); a caller without it gets `MCP-AUTHZ-001`. `endpoint` must be a full `http(s)` URL and `https://` outside Development - a BYO key is sent to it as a Bearer token, and a plain `http://` endpoint exposes the key on the network path. Changing only `endpoint` without also supplying a new `apiKey` clears the stored key (and its fingerprint/`keyUpdatedAtUtc`) whenever the endpoint's *host* actually changes, rather than silently continuing to send the old key to a new host; a path- or scheme-only edit to the same host leaves the key untouched.

Tooling is declarative, not free-form HTTP from the model. Tenants expose resources as **capability bindings** (their URL/auth). FlowOS hosts these resource plugins and prefetches read results into `DecisionPacket.Data.ToolResults` before the agent decides:

| Plugin | `agentTools` | Tenant capability example | When it runs |
|---|---|---|---|
| `LookupRecord` | `LookupRecord:crm.customer.get.v1` | Customer/job/entity GET | Prefetch (read) |
| `QueryRecords` | `QueryRecords:inventory.parts.query.v1` | Search/list | Prefetch (read) |
| `FetchDocument` | `FetchDocument:docs.quote.get.v1` | Quote PDF / attachment | Prefetch (read) |
| `SearchKnowledge` | `SearchKnowledge:kb.policy.search.v1` | KB / policy corpus | Prefetch (read) |
| `CheckPolicy` | `CheckPolicy:policy.approval-limit.v1` | Approval limits / rules | Prefetch (read) |
| `InvokeCapability` | `capability:payment.refund.v1` | Any write API | Declared only — not prefetched |
| Email / Slack / WhatsApp / Webhook / Notification | `Webhook`, `plugin:Email` | Notify | Declared only — not prefetched |

Register each capability with `register_capability_binding`. The agent never receives the endpoint URL. Remote calls require `FlowOS:Capabilities:EnableRemoteInvoke=true`. DecisionProvider plugins stay deterministic expression routers — they are not LLMs.

Until an LLM HTTP client is wired, FlowOS still hosts `RiskAnalysisAgent` as the fixture runtime while storing the tenant provider settings on the packet.

## Tenant-owned prompts (create / edit)

Users create and edit prompts independently of the workflow JSON. Dashboard: **Agent Prompts**. MCP: `upsert_agent_prompt` / `list_agent_prompts` / `get_agent_prompt` (also `register_plugin_binding` with `bindingType: prompt`).

```json
{
  "bindingType": "prompt",
  "sourceName": "quote-approval",
  "providerName": "markdown",
  "configuration": {
    "title": "Quote approval",
    "system": "You are a service advisor assistant. Only suggest legal nextSteps events.",
    "instructions": "Approve if the quote is within 15% of estimate; otherwise request revision."
  }
}
```

Point the waiting step at it with `agentPrompt: "quote-approval"`. FlowOS loads title/system/instructions into `DecisionPacket.Prompt`. Template `decisionGuideline` remains an optional fallback. REST: `GET/PUT /api/plugin-bindings`.

Do **not** replace a waiting HumanTask with a Decision `Default` skip so the AI "advances" the graph. That reopens dual-kernel drift.

## Agent contract

1. **Input**: `DecisionPacket` (also projected as `AgentContext`) — Prompt / Data / Tools / redacted Provider. Never a raw tenant dump and never the API key.
2. **Output**: `AgentResult` — an insight string, optional structured data, and optional `SuggestedAction`s. Only legal `nextSteps` events are kept. For `TenantLlmWorkflowAgent` (the hosted LLM loop), the model's JSON reply is `insight` (narrative, **hard-capped at 2000 characters** — a reply over that cap fails the run and is never silently truncated), `actions`, and an optional top-level `structuredOutput` object alongside `insight`/`actions` — business data extracted or computed for this step (amounts, line items, risk scores, or a list of records). `structuredOutput` has **no character cap** — put data there, not in `insight`. When present it becomes `AgentResult.StructuredData`; it is best-effort beyond that — a missing or malformed `structuredOutput` never fails the suggestion, `StructuredData` is just `{}`. Nested values (arrays, nested objects, strings) in `StructuredData` are converted to plain CLR types before the result is returned over MCP, not left as raw `System.Text.Json.JsonElement`s — the MCP response is serialized with Newtonsoft.Json, which cannot read a boxed `JsonElement` by value and would otherwise reflect it into a `{"ValueKind": N}` stub instead of the real data.

   An over-length `insight` fails with a dedicated `FailureCode` of `FIELD_OVERFLOW`, plus `FailureField` (`"insight"`), `FailureLimit` (`2000`), and `FailureActualLength` (the model's actual length) on `AgentResult` — a caller can branch on `FailureCode` instead of parsing `FailureReason`'s free text to tell "insight was too long" apart from any other malformed reply. Every other parse failure (invalid JSON, missing or wrong-typed fields, a tool loop that never terminates, a suggestion outside the legal `nextSteps` set) still fails with the generic `INVALID_MODEL_OUTPUT`, and `FailureField`/`FailureLimit`/`FailureActualLength` stay `null` for those.
3. **Side effects during reasoning**: none. Auto-commit is hosted by FlowOS after the agent returns.

```csharp
public interface IWorkflowAgent
{
    Task<AgentResult> ExecuteAsync(AgentContext context);
    Task<AgentResult> ExecuteAsync(DecisionPacket packet, CancellationToken cancellationToken = default);
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

* **Correct**: "Agent suggests Escalation (95% confidence)." If `autoCommit` allows `EVT-ESCALATE` at that confidence, FlowOS publishes it; otherwise a human clicks the Smart Action, which publishes the actual event through the normal API.
* **Incorrect**: the agent directly calling something equivalent to `Approve()`, or an MCP chat agent inventing `publish_event` from free text.

These suggestions appear in the UI as **Smart Actions** — buttons the user must explicitly click to execute.

## Business use cases

| Agent Type | Trigger | Logic | Suggestion |
|---|---|---|---|
| Risk Analyzer | Expense submission | Checks amount, history, category against fraud patterns | `EVT-ESCALATE` or `EVT-FLAG-FRAUD` |
| Auto-Approver | Low-value request | Verifies budget/policy compliance | `EVT-APPROVE` |
| Compliance Bot | Contract review | Scans document for missing clauses | `EVT-REQUEST-CHANGES` |
| Router | Support ticket | Classifies sentiment/topic | `EVT-ROUTE-TIER2` |

## Governance guarantees

* **Read-only context** — agents receive a DecisionPacket; they cannot cause side effects during reasoning.
* **Explicit intent** — agents output structured events from legal `nextSteps`, never free-form commands.
* **State Machine enforcement still applies** — auto-commit still goes through `PublishEventCommand` and `WorkflowEngine`. An agent's suggestion carries no extra authority beyond the auto-commit policy.

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
