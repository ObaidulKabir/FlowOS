# 20. Hosted LLM and tenant automation policy

This is the durable commercial and product policy for **AI task automation** on waiting Agent/Either steps. Subscription plans and MCP inclusion stay in [Chapter 18](18-commercial-and-mcp-entitlements.md). Dual-kernel and auto-commit law stay in [Chapter 7](07-ai-agents-and-insights.md). OS-1 honesty stays in [Chapter 19](19-os-release-gate.md). How this loop compares to Temporal, Camunda, Step Functions, Conductor, and n8n is [Chapter 21](21-competitive-comparison.md).

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

## Hybrid execution and durable audit

FlowOS has one governed execution path with two caller experiences:

- **API workflow entry is queued.** Entering a waiting Agent/Either step stages a PostgreSQL `AgentTaskJob`. The API worker uses an expiring claim, and a distributed lease prevents two workers from calling the provider for the same tenant/instance/step.
- **MCP is synchronous to the caller, not a separate engine.** `run_agent_task` and `suggest_agent_action` enqueue/claim through the same durable queue and lease path, then wait for a terminal result. An MCP timeout does not delete the job; the API/MCP worker can still finish it.
- Every attempt receives an `executionId`; queue ownership/retry uses `jobId`. Successful MCP run responses return both. `AgentExecutionRecord` stores only governed audit metadata and sanitized failure codes—not API keys, prompt bodies, model response bodies, event payloads, or raw provider exceptions.
- PostgreSQL is the source of truth for jobs, claims, leases, hosted daily usage, and execution audit. Startup reconciliation can safely reclaim expired work. API and MCP must point at the same database to share that truth.

Actor provenance is explicit. An automatic commit publishes with `ActorId = Agent:{agentId}`. A later human event keeps the authenticated human actor. Evaluation may identify that later event as an override, but it never rewrites either event.

## Quota and BYO accounting

The hosted daily cap is a persistent per-tenant, per-model **UTC-day** reservation/finalization counter in PostgreSQL. A reservation is made before the platform-key call and finalized with success/failure and token totals. Restarting or adding an API/MCP replica does not reset the day.

`flowos-hosted` consumes this hosted quota. Tenant BYO providers (`openai`, `anthropic`, `azure-openai`, `google`, `custom`) do **not** reserve or meter FlowOS-hosted usage; their vendor account and key pay for those calls.

Simulation is separate: `simulate_workflowclass` / `simulate_context_binding` uses the deterministic evaluator (`autoAdvanceAgents` / optional `simulatedAgent`). It never creates a provider usage reservation and never calls a paid model.

## Host configuration (operators)

Set the platform key on **both** the API host and the MCP host. The dashboard has no Admin field for this. Never commit the key. Never paste it into this chat, MCP, a blueprint, or Agent Context.

Hosted provider is **OpenAI only**. Model default is `gpt-4o-mini`. Anthropic/Azure/Google are tenant BYO, not the FlowOS server default.

### Settings

| Setting | Purpose | Default |
| --- | --- | --- |
| `FLOWOS_HOSTED_LLM_API_KEY` (preferred) or `FlowOS:HostedLlm:ApiKey` | Platform OpenAI secret | empty (feature off until set) |
| `FLOWOS_HOSTED_LLM_ENABLED` / `FlowOS:HostedLlm:Enabled` | Master switch | `true` |
| `FLOWOS_HOSTED_LLM_MODEL` / `FlowOS:HostedLlm:Model` | OpenAI model | `gpt-4o-mini` |
| `FLOWOS_HOSTED_LLM_ENDPOINT` / `FlowOS:HostedLlm:Endpoint` | Optional chat-completions URL | OpenAI default via adapter |
| `FLOWOS_HOSTED_LLM_MAX_PER_DAY` / `FlowOS:HostedLlm:MaxCompletionsPerDay` | Per-tenant UTC-day cap | `200` |

`appsettings.json` keeps `ApiKey` empty on purpose. MCP has no HostedLlm appsettings file — **environment variables are required on MCP**.

### Setup procedure

1. Create an OpenAI API key on the FlowOS operator account (not a tenant account).
2. Put it in a gitignored `.env` next to compose, or in the host secret store. Example `.env` (do not commit):

```
FLOWOS_HOSTED_LLM_API_KEY=sk-...
FLOWOS_HOSTED_LLM_MODEL=gpt-4o-mini
FLOWOS_HOSTED_LLM_MAX_PER_DAY=200
FLOWOS_HOSTED_LLM_ENABLED=true
```

3. Apply it to **API and MCP** (same values). Compose already forwards these names (`docker-compose.yml`, `docker-compose.prod.yml`, sandbox). Dual live hosts (production and staging) each need their own copy.

### Local setup (Windows / PowerShell)

Development does **not** enforce billing (`FLOWOS_BILLING_ENFORCE` off unless you set it). You do not need Admin plan activation on localhost. You still need the OpenAI key on the process that will call the model.

Do not put the key in `appsettings.json`. Do not paste it into chat or the dashboard `flowos-hosted` row.

**Path A — dashboard + API (enough to run QuoteAutoReview live).** After `start` / `publish_event`, the API stages durable Agent work and its background worker processes the queue.

Terminal 1 — API (`http://localhost:5183`):

```
cd C:\Users\HP\Documents\trae_projects\FLowOS\src\FlowOS.Api
$env:FLOWOS_HOSTED_LLM_API_KEY = "<your-openai-key>"
$env:FLOWOS_HOSTED_LLM_MODEL = "gpt-4o-mini"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --UseInMemoryDatabase=true
```

Terminal 2 — dashboard (`http://localhost:5173`):

```
cd C:\Users\HP\Documents\trae_projects\FLowOS\apps\dashboard
npm run dev
```

Sandbox or register a tenant. Open **QuoteAutoReview**. Launch a live instance (not the in-memory simulator). Publish `EVT-SUBMIT` with Amount **≤ 1500**. The instance should land on `AgentReview` and the API should call hosted OpenAI. Amount **> 1500** goes to Advisor (human) — no model call.

`simulate_*` / Visual Demo Simulator never spends the key. A green simulate does not prove local setup.

**Path B — also run MCP tools** (`list_available_agents`, `run_agent_task`). MCP has its own process and its own store if you use in-memory. For MCP to see dashboard instances, both API and MCP must use the **same Postgres**.

Use `appsettings.Development.json` defaults (`localhost:5432`, database `flowos`, user `postgres` / `password`) or `docker compose up -d postgres`.

Terminal 1 — API against Postgres (omit `--UseInMemoryDatabase=true`).

Terminal 3 — MCP HTTP on `http://127.0.0.1:8081`:

```
cd C:\Users\HP\Documents\trae_projects\FLowOS
$env:MCP_TRANSPORT = "http"
$env:ASPNETCORE_URLS = "http://127.0.0.1:8081"
$env:MCP_API_KEY = "disabled"
$env:MCP_ROLE = "Admin"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:FLOWOS_HOSTED_LLM_API_KEY = "<your-openai-key>"
$env:FLOWOS_HOSTED_LLM_MODEL = "gpt-4o-mini"
dotnet run --project src\FlowOS.MCP\FlowOS.MCP.csproj
```

Then MCP `list_available_agents` should show `flowos-hosted` `hasApiKey: true`. After `EVT-SUBMIT` to `AgentReview`, `run_agent_task` with `workflowInstanceId` (omit `agentId`).

**Compose local:** put the key in gitignored `.env` at the repo root, then `docker compose up -d --force-recreate flowos-api flowos-mcp`. API is `http://localhost:5183`, MCP `http://localhost:8081`. Dashboard compose is port `3000` unless you keep using Vite on `5173` against that API.

After you change the key, stop and start the same terminals (env is per-process).

   Docker:

```
docker compose up -d --force-recreate flowos-api flowos-mcp
```

   Production compose:

```
docker compose -f docker-compose.prod.yml up -d --force-recreate flowos-api flowos-mcp
```

4. Restart both processes after any key change. The runtime reads config at lease time from `IConfiguration`; a dead process still has the old env until recreate.
5. Activate the tenant: Admin `POST /api/admin/tenants/{id}/plan` with Managed or Enterprise and `BillingStatus=Active`. Trial returns `MCP-PLAN-REQUIRED` and never spends the platform key.
6. Confirm without printing the secret:
   - MCP `list_available_agents` — `flowos-hosted` has `hasApiKey: true`.
   - `preview_agent_context` on QuoteAutoReview / `AgentReview` — Provider shows `hasApiKey`, never `apiKey`.
   - Live path only: start QuoteAutoReview, `publish_event` `EVT-SUBMIT` with Amount ≤ 1500, then `run_agent_task`. `simulate_*` never calls OpenAI.

Do **not** paste a key into Application → AI Context → Providers for `flowos-hosted` (it is dropped). That screen is for optional tenant BYO (`openai`, `anthropic`, …).

## Error codes

| Caller code | Persisted execution code | Meaning | Tenant/operator action |
| --- | --- | --- | --- |
| `MCP-PLAN-REQUIRED` | `ENTITLEMENT_DENIED` | Trial or unpaid; runtime and hosted tokens blocked | Activate Managed/Enterprise (`BillingStatus=Active`) |
| `MCP-HOSTED-LLM-UNAVAILABLE` | `PROVIDER_CONFIGURATION` | Host key missing, hosted disabled, or provider cannot be resolved | Operator sets `FLOWOS_HOSTED_LLM_API_KEY` on the executing host; verify the provider alias |
| `MCP-HOSTED-LLM-QUOTA` | `HOSTED_QUOTA_DENIED` | Tenant hit today's persistent hosted cap | Wait for UTC midnight, raise the cap, or bind BYO |
| provider-specific response | `PROVIDER_AUTH`, `PROVIDER_RATE_LIMIT`, `PROVIDER_TIMEOUT`, `PROVIDER_UNAVAILABLE`, or `INVALID_MODEL_OUTPUT` | Provider transport/auth/output failed after resolution | Check the provider binding/vendor, then follow retry policy |

List/get/preview return `hasApiKey` for hosted when the **platform** key is present. They never return the secret. `upsert_agent_provider` / `register_plugin_binding` with `flowos-hosted` **drops** any tenant `apiKey`.

Provider failure is not insight success. A failed resolution, quota/entitlement denial, transport failure, or invalid model result is persisted as a failed execution and must never emit a successful `AgentInsightGenerated`. Successful suggestions keep the existing insight behavior.

## Execution history and governed evaluation

Authenticated, tenant-scoped read surfaces:

- API: `GET /api/agents/{workflowInstanceId}/history` (also `/api/agents/instances/{workflowInstanceId}/history`) and `GET /api/agents/metrics?fromUtc=...&toUtc=...`.
- MCP: `get_agent_execution_history` and `get_agent_evaluation_metrics`.

History is bounded and allowlisted. It can return execution/job/instance IDs, step and actor, mode, provider alias/name/model, **prompt alias only**, runtime/version identifiers, timestamps/duration, terminal/failure/HTTP status, tokens, suggestion/confidence, commit/park/override, and outcome labels. It never returns secrets, prompt content, response content, event payloads, idempotency keys, claimant internals, or raw/sanitized failure messages.

Metrics use a requested half-open UTC execution window (`fromUtc` inclusive, `toUtc` exclusive; maximum 90 days). They report runs, successes/failures, commits, parks, hosted quota denials, overrides, suggestion/evaluated/matched counts, latency average/p50/p95, input/output tokens, and provider/model breakdown. Confidence calibration uses explicit bins and Brier score only for suggestions with an outcome derivable from durable execution state or immutable workflow events.

An outcome with no immutable evidence is labeled `unevaluated`; a run with no suggestion is `not_applicable`. A mismatching workflow transition from the same step may be labeled an override with its recorded actor. Metrics are evidence, not a feedback actuator: FlowOS does **not** train a model, edit a prompt, move a threshold, mutate policy, or rewrite execution/event history automatically.

## Troubleshooting

1. Save `jobId` and `executionId` from `run_agent_task` / `suggest_agent_action`.
2. Read execution history for the instance. If no record exists yet, the job may still be pending/claimed; MCP waiting is synchronous but the durable worker is authoritative.
3. `ENTITLEMENT_DENIED`: activate the tenant plan. No paid provider request was made.
4. `HOSTED_QUOTA_DENIED`: inspect the UTC date and hosted cap. BYO is the non-host-metered alternative.
5. `PROVIDER_CONFIGURATION`: confirm API and MCP use the same PostgreSQL database and that the platform key exists on the process that actually claimed the job.
6. Provider codes: fix credentials/vendor availability or wait for the retry schedule. Do not treat a failed run as an Agent insight.
7. A green `simulate_*` run proves deterministic Law/Work/auto-commit behavior only. It does not verify network access, a secret, hosted entitlement, quota, or the paid provider.

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
- Metered overage billing (today: hard cap, then BYO).
- Enterprise-only higher default cap (today: one `MaxCompletionsPerDay` for all paid tenants).
- Real paid-provider smoke verification remains a separate final operational check; deterministic tests must not spend the platform key.
