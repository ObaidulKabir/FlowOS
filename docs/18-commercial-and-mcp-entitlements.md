# 18. Commercial policy and MCP entitlements

FlowOS sells **usage plus progression**, not seats first and not a per-call MCP tax.

A developer should be able to try, then build, then grow without feeling punished for success:

**Free → Starter → Builder → Team → Growth → Scale → Enterprise**

MCP is included on every package. Simulation, lint, validate, and recovery stay available at the bottom of the ladder. Higher packages add volume, retention, concurrency, collaboration, and support.

Until self-serve checkout and meters are live, a platform Admin activates a paid package with `POST /api/admin/tenants/{id}/plan`. Register without activation stays design-time (`MCP-PLAN-REQUIRED` on runtime tools). Guest sandbox remains a disposable playground with no production credentials.

## What we charge for

Three meters. We will not charge per retry, simulation, replay, or compensation.

| Meter | Counts | Weight |
| --- | --- | --- |
| **Workflow publications** | New WorkflowClass, new version, or a material republish. Show **active published workflows** separately so republishing the same graph is not 30 units. | High value, relatively scarce |
| **Events published** | Business activity (`OrderCreated`, `PaymentCompleted`, …). Main scale meter. | High |
| **MCP tool calls** | Control-plane interaction from Cursor, Claude, or a custom agent. | Generous included allowance, inexpensive overage |

MCP must stay cheap. Asking “why did this fail?” should never feel like spending a credit.

Do **not** launch a blended “Flow Unit” currency. Keep the three meters visible. A blended unit can wait until real usage distributions exist.

## Published packages

Starting points, not frozen infrastructure math. Annual billing is two months free.

| | Free | Starter | Builder | Team | Growth | Scale | Enterprise |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Monthly | $0 | $9 | $29 | $79 | $199 | $499 | Custom |
| Annual equivalent | — | ~$90 | ~$290 | ~$790 | ~$1,990 | ~$4,990 | Custom |
| Publications | 2 | 10 | 30 | 100 | 300 | 1,000 | Custom |
| Events / month | 2,500 | 15K | 75K | 300K | 1.5M | 10M | Custom |
| MCP calls / month | 2,500 | 15K | 75K | 300K | 1.5M | 10M | Custom |
| Active workflows | 2 | 5 | 15 | 50 | 150 | 500 | Custom |
| Projects / tenants | 1 | 1 | 3 | 10 | 25 | 100 | Custom |
| Concurrent executions | 2 | 5 | 10 | 25 | 75 | 250 | Custom |
| Event retention | 7 days | 14 days | 30 days | 90 days | 180 days | 365 days | Custom |
| Team members | 1 | 1 | 3 | 10 | 25 | 50 | Custom |
| Support | Community | Community | Standard | Priority | Priority | Dedicated | Dedicated + SLA |

Enterprise is not a hidden price. It includes private or VPC deploy, SSO/SAML, custom retention, custom limits, MCP policies, data residency, security review, and a written SLA. Typical starting range $1,500–$5,000+ / month.

A **student / local developer** package (for example ৳499–৳799 / month) is an acquisition channel, not a permanently inferior Bangladesh edition. Graduates move onto Starter or Builder with the same product.

## Soft overage

Do not hard-stop a customer at 105% of a limit.

Intended path: continue through a modest overage (for example $0.50 per additional 10K events on Builder), then recommend the next package. MCP overage stays inexpensive. The usage dashboard should show this month’s publications, events, and MCP calls, plus a projected-next-month hint.

The usage dashboard and automatic overage billing are **not shipped yet**. Until they are, a paid Active tenant has the full runtime; limits are commercial policy, not an enforced meter.

## Runtime entitlement today

The kernel still has three stored plans: `Trial`, `Managed`, and `Enterprise`.

| Commercial package | Stored plan | Runtime |
| --- | --- | --- |
| Guest sandbox | none (playground session) | Disposable demo only |
| Free (register, not activated) | `Trial` | Design-time: simulate, lint, validate, generate, drafts |
| Starter, Builder, Team, Growth, Scale | `Managed` + `BillingStatus=Active` | Full runtime |
| Enterprise | `Enterprise` + `BillingStatus=Active` | Full runtime + custom terms |

Design-time vs runtime is **what the tenant may execute**, not “MCP vs API”:

- Design-time (Free / Trial): simulate, lint, validate, describe, explain, create/update drafts, create/update draft context bindings, `simulate_context_binding`, and public catalog reads.
- Runtime (paid Active): start instances, publish events, complete tasks, publish workflow classes, activate/archive context bindings.

Local Development and `MCP_API_KEY=disabled` do not enforce billing. Staging and Production do, unless `FLOWOS_BILLING_ENFORCE=false`.

Activate with `POST /api/admin/tenants/{id}/plan` (`plan`: Managed or Enterprise, `billingStatus`: Active). Existing production tenants remain Managed + Active.

Before locking the numbers, instrument: MCP calls per active developer, events per workflow, publications per month, concurrency, retention cost, and infrastructure cost per 10K/100K events. Aim for most customers to sit comfortably inside a package, a minority to approach the next tier, and the highest-usage tenants to create expansion revenue.

## FlowOS hosted OpenAI

Canonical policy: [Chapter 20 — Hosted LLM and tenant automation policy](20-hosted-llm-and-automation-policy.md).

Paid Active tenants get a platform OpenAI model (`flowos-hosted`) without an AI Context key. Set `FLOWOS_HOSTED_LLM_API_KEY` on the host. Trial cannot spend it. Daily cap default 200 (`MCP-HOSTED-LLM-QUOTA`). BYO keys remain optional.
