# 20. Hosted LLM and tenant automation policy

This is the durable commercial and product policy for **AI task automation** on waiting Agent/Either steps. Subscription plans and MCP inclusion stay in [Chapter 18](18-commercial-and-mcp-entitlements.md). Dual-kernel and auto-commit law stay in [Chapter 7](07-ai-agents-and-insights.md). OS-1 honesty stays in [Chapter 19](19-os-release-gate.md).

**One-line policy:** A paid plan runs automation out of the box on FlowOS-hosted OpenAI with a daily cap. A tenant API key is optional (compliance / extra volume), not required to start.

## Why this policy

| Goal | Rule |
| --- | --- |
| Tenant ease | No OpenAI account and no AI Context key to complete a Quote (or any Agent step). Register → pay (or Admin activates Managed/Enterprise) → start instance → `run_agent_task`. |
| FlowOS revenue | Subscription is the product (`Managed Cloud $299/month` or Enterprise). Hosted tokens are a **capped convenience** inside that fee, not an unlimited model. |
| FlowOS cost control | Trial never spends the platform key. Daily `MaxCompletionsPerDay` (default 200) stops one tenant from draining the host bill. Over cap → BYO or wait. |
| Compliance | BYO (`openai`, `anthropic`, `azure-openai`, `google`, `custom`) still wins when the step alias is a tenant binding with a key. Regulated tenants keep residency and their own vendor contract. |

Do **not** sell BYO-only as the default (tenants stall on a key). Do **not** include unlimited hosted calls in the flat fee. Do **not** market `flowos-risk` as the paid AI product (it is a no-key fixture).

## What FlowOS is selling

FlowOS sells the **operating system** (Law, Work, inbox, simulation, MCP, hosted DecisionPacket loop). The hosted LLM is how a paid tenant uses that loop without bringing a vendor key.

```
Paid tenant → Agent/Either wait → DecisionPacket
  → flowos-hosted  (platform OpenAI key + quota)     [default]
  → or BYO openai/anthropic/… (tenant key)           [opt-in]
  → or flowos-risk (RiskAnalysisAgent, no HTTP LLM)  [fixture]
  → AutoCommitPolicy → publish or park
```

Checkout (`L-PAY`) can wait. Admin `POST /api/admin/tenants/{id}/plan` already unlocks runtime. Hosted OpenAI uses the same paid-Active gate.

## Provider kinds

| `providerName` / alias | Who pays the model | Tenant key | When to use |
| --- | --- | --- | --- |
| `flowos-hosted` | FlowOS (capped) | Never | Paid default. QuoteAutoReview `AgentReview`. |
| `openai` / `anthropic` / `azure-openai` / `google` / `custom` | Tenant | Yes (write-only on the tenant binding) | Residency, own Azure/OpenAI, or over hosted quota. |
| `flowos-risk` | Nobody | No | Fixture / no host key. Not a substitute for hosted OpenAI. |

Step field `agentProvider` is only an alias. Factory order:

1. Explicit `flowos-risk` → `RiskAnalysisAgent`
2. Explicit `flowos-hosted`, or no provider **and** the host key is configured → platform OpenAI (`TenantLlmWorkflowAgent` + OpenAI adapter)
3. BYO kind with a tenant binding → tenant key
4. Otherwise → `RiskAnalysisAgent`

`simulate_workflowclass` / `simulate_context_binding` never call OpenAI. They only prove graph, Law, inbox, and `AutoCommitEvaluator`.

## Tenant experience

1. Trial: design, lint, validate, simulate. No live `run_agent_task`, no platform tokens (`MCP-PLAN-REQUIRED`).
2. Paid Active: start `QuoteAutoReview` (or any Agent/Either wait), publish the human gate (`EVT-SUBMIT` when Amount ≤ 1500), call `run_agent_task` with only `workflowInstanceId`.
3. Dashboard: **Application →** select workflow → **AI Context → Providers**. `flowos-hosted` is listed first and does **not** ask for an API key. The top **Keys** tab is the FlowOS API key, not OpenAI.
4. Optional BYO: `upsert_agent_provider` with `openai` (or dashboard Providers) and point the step at that alias.

MCP prompt: `automate_waiting_task_with_ai_agent`.  
MCP resource: `flowos://guides/ai-task-automation`.

## Host configuration (operators)

Set the platform key on the **API and MCP hosts**. Never commit it. Never paste it into MCP chat, a blueprint, or Agent Context.

| Setting | Purpose | Default |
| --- | --- | --- |
| `FLOWOS_HOSTED_LLM_API_KEY` or `FlowOS:HostedLlm:ApiKey` | Platform OpenAI secret | empty (feature off until set) |
| `FlowOS:HostedLlm:Enabled` / `FLOWOS_HOSTED_LLM_ENABLED` | Master switch | `true` |
| `FlowOS:HostedLlm:Model` / `FLOWOS_HOSTED_LLM_MODEL` | OpenAI model | `gpt-4o-mini` |
| `FlowOS:HostedLlm:Endpoint` / `FLOWOS_HOSTED_LLM_ENDPOINT` | Optional override | OpenAI default via adapter |
| `FlowOS:HostedLlm:MaxCompletionsPerDay` / `FLOWOS_HOSTED_LLM_MAX_PER_DAY` | Per-tenant UTC-day cap | `200` |

`appsettings.json` keeps `ApiKey` empty on purpose. Staging/Production inherit the same HostedLlm block.

## Error codes

| Code | Meaning | Tenant action |
| --- | --- | --- |
| `MCP-PLAN-REQUIRED` | Trial or unpaid; runtime and hosted tokens blocked | Activate Managed/Enterprise (`BillingStatus=Active`) |
| `MCP-HOSTED-LLM-UNAVAILABLE` | Host key missing or hosted disabled | Operator sets `FLOWOS_HOSTED_LLM_API_KEY` |
| `MCP-HOSTED-LLM-QUOTA` | Tenant hit today's hosted cap | Wait for UTC midnight, raise the cap, or bind BYO |

List/get/preview return `hasApiKey` for hosted when the **platform** key is present. They never return the secret. `upsert_agent_provider` / `register_plugin_binding` with `flowos-hosted` **drops** any tenant `apiKey`.

## What must not change

- Dual-kernel: the model may only return a legal `nextSteps` event.
- `autoCommit.allowedEvents` is a subset of `nextSteps`. TimeoutEvent / SLA reminders are never auto-committed.
- Payment does not skip Law or park-on-low-confidence.
- The platform key never appears in DecisionPacket, MCP tool results, dashboard lists, or git.
- This is **not** a FlowOS-trained foundation model. It is FlowOS holding an OpenAI key and metering it.

## Sample

`QuoteAutoReview` / `AgentReview`: `actor: Agent`, `agentPrompt: quote-approval`, `agentProvider: flowos-hosted`, `autoCommit.allowedEvents: [EVT-ACCEPT]`. Amount > 1500 routes to Advisor (human).

Proof: `tests/FlowOS.UnitTests/Agents/FlowOsHostedLlmTests.cs`, `tests/FlowOS.UnitTests/Workflows/QuoteAutoReviewSimulationTests.cs`.

## Later (does not change this policy)

- `L-PAY` self-serve checkout (Admin-assigned plans already gate hosted OpenAI).
- Persistent quota store (v1 is an in-process daily counter; multi-instance hosts should treat the cap as best-effort until a shared store exists).
- Metered overage billing (today: hard cap, then BYO).
- Enterprise-only higher default cap (today: one `MaxCompletionsPerDay` for all paid tenants).
