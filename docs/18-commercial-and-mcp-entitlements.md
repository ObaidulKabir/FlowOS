# 18. Commercial policy and MCP entitlements

FlowOS sells a **tenant subscription**. MCP is included: a paid Managed Cloud or Enterprise tenant includes dashboard, REST API, tenant API keys, and every MCP tool. There is no per-call or per-transition overage in this model.
No usage fees are charged for MCP calls; hosted LLM automation is governed by the daily completion cap described below.

## Plans

- **Playground ($0):** guest sandbox dashboard, public `GET` discovery, `initialize`, and `tools/list`. No durable production tenant and no production API keys.
- **Trial (free, after register):** design-time only. Drafts, lint, validate, simulate (including `simulate_context_binding` against a draft template), schema/explain, listing public templates, and create/update of draft context bindings work with a tenant API key. Runtime execution is denied with `MCP-PLAN-REQUIRED` (HTTP 402).
- **Managed Cloud ($299 / month):** full runtime. Includes MCP `start_workflow`, `publish_event`, `complete_task`, publish/activate, matching REST endpoints, and **FlowOS hosted OpenAI** (`flowos-hosted`) with a daily completion cap (`FlowOS:HostedLlm:MaxCompletionsPerDay`, default 200). No per-transition fees. Tenants may still bind a BYO key.
- **Enterprise (custom):** same runtime entitlement; commercial terms via `admin@flowosbd.com`.

Until a payment provider is wired, a platform Admin activates a plan with `POST /api/admin/tenants/{id}/plan` (`plan`: Managed or Enterprise, `billingStatus`: Active). Existing tenants were grandfathered to Managed + Active.

## Design-time vs runtime

Free vs paid is **what the tenant may execute**, not “MCP vs API”:

- Design-time (Trial): simulate, lint, validate, describe, explain, create/update drafts, create/update draft context bindings, `simulate_context_binding`, and public catalog reads.
- Runtime (paid Active Managed/Enterprise): start instances, publish events, complete tasks, publish workflow classes, activate/archive context bindings, and other mutating/irreversible tools.

Local Development and `MCP_API_KEY=disabled` do not enforce billing. Staging and Production do, unless `FLOWOS_BILLING_ENFORCE=false`.

## FlowOS hosted OpenAI

Canonical policy: [Chapter 20 — Hosted LLM and tenant automation policy](20-hosted-llm-and-automation-policy.md).

Paid Active tenants get a platform OpenAI model (`flowos-hosted`) without an AI Context key. Set `FLOWOS_HOSTED_LLM_API_KEY` on the host. Trial cannot spend it. Daily cap default 200 (`MCP-HOSTED-LLM-QUOTA`). BYO keys remain optional.
