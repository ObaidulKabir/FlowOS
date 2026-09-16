# 18. Commercial policy and MCP entitlements

FlowOS sells a **tenant subscription**. MCP is included: a paid Managed Cloud or Enterprise tenant includes dashboard, REST API, tenant API keys, and every MCP tool. There is no per-call or per-transition overage in this model.

## Plans

- **Playground ($0):** guest sandbox dashboard, public `GET` discovery, `initialize`, and `tools/list`. No durable production tenant and no production API keys.
- **Trial (free, after register):** design-time only. Drafts, lint, validate, simulate, schema/explain, and listing public templates work with a tenant API key. Runtime execution is denied with `MCP-PLAN-REQUIRED` (HTTP 402).
- **Managed Cloud ($299 / month):** full runtime. Includes MCP `start_workflow`, `publish_event`, `complete_task`, publish/activate, and matching REST endpoints. No usage fees.
- **Enterprise (custom):** same runtime entitlement; commercial terms via `admin@flowosbd.com`.

Until a payment provider is wired, a platform Admin activates a plan with `POST /api/admin/tenants/{id}/plan` (`plan`: Managed or Enterprise, `billingStatus`: Active). Existing tenants were grandfathered to Managed + Active.

## Design-time vs runtime

Free vs paid is **what the tenant may execute**, not “MCP vs API”:

- Design-time (Trial): simulate, lint, validate, describe, explain, create/update drafts, public catalog reads.
- Runtime (paid Active Managed/Enterprise): start instances, publish events, complete tasks, publish workflow classes, activate/archive context bindings, and other mutating/irreversible tools.

Local Development and `MCP_API_KEY=disabled` do not enforce billing. Staging and Production do, unless `FLOWOS_BILLING_ENFORCE=false`.
