# 21. Competitive comparison

This chapter is the durable OS-1 scoreboard against the platforms buyers actually put next to FlowOS. The live UI is the same data: landing **Platform Benchmark**, tenant/admin **vs Competitors**. Source: `apps/dashboard/src/comparisonMatrix.ts`. Hosted-OpenAI policy stays in [Chapter 20](20-hosted-llm-and-automation-policy.md). The OS claim stays in [Chapter 19](19-os-release-gate.md).

Scores are **Native / Custom / —** against FlowOS OS-1 pillars, not market share or hyperscale. Camunda is the closest OS-shaped rival. n8n is the wrong class.

## Buy FlowOS when

The buyer needs tenant SaaS where AI agents author, simulate, and run **legal** work under dual-kernel Law plus a HumanTask inbox, over MCP — including a paid-plan hosted OpenAI default so Agent/Either steps run without a tenant LLM key.

## Buy someone else when

| If the buyer needs | Buy instead | Why |
| --- | --- | --- |
| Polyglot durable code + replay | Temporal.io | Workflows are real programs; FlowOS is JSON + dual-kernel, not a worker SDK. |
| BPMN 2.0 / DMN + analyst Tasklist | Camunda 8 | Camunda is the notation standard; FlowOS inbox is role-filtered REST. |
| Zero-ops AWS service mesh | AWS Step Functions | IAM + 200 AWS integrations; FlowOS bindings are tenant plugins. |
| Hyperscale worker DAGs | Conductor / Orkes | Polling task queues at Netflix scale; FlowOS is a tenant process OS. |
| Departmental SaaS recipes | n8n / Zapier | Connector speed; they are not legal-state operating systems. |

## OS-1 matrix

| Pillar | FlowOS | Temporal.io | Camunda 8 | AWS Step Functions | Conductor / Orkes | n8n / Zapier |
| --- | --- | --- | --- | --- | --- | --- |
| Identity | Native | Custom | Native | Native | Custom | Custom |
| Dual-kernel | Native | — | Custom | Custom | — | — |
| Policy | Native | Custom | Native | Custom | — | — |
| Inbox | Native | Custom | Native | Custom | Custom | Custom |
| Integrations | Native | Custom | Native | Native | Custom | Native |
| AI loop | Native | Custom | Custom | Custom | — | Custom |
| Simulation | Native | Custom | Custom | Custom | Custom | Custom |
| Ops | Native | Native | Native | Native | Native | Custom |
| Commercial | Native | Custom | Native | Native | Custom | Custom |

## Per platform

### FlowOS — business automation OS

**Pick when:** Tenant SaaS where agents must author, simulate, and run legal work under Law + inbox over MCP.

**Wins:** Declarative JSON, native MCP, fail-closed Law, hosted DecisionPacket / autoCommit, paid `flowos-hosted` OpenAI (daily cap) plus optional BYO, side-effect-free simulation, .NET 8 + Postgres.

**Loses:** Not BPMN. Not Temporal/Zeebe scale. No 200+ AWS catalog. OIDC, Stripe checkout, OTEL, and SCIM are later.

### Temporal.io — durable execution (code-as-workflow)

**Pick when:** Engineering teams writing Go/TS/Java/Python workflows that must survive crashes with deterministic replay.

**Wins:** Durable execution, multi-language SDKs, retries, hyperscale.

**Loses vs the OS bar:** Deterministic-code tax. No separate FSM Law. No native HumanTask OS. No MCP authoring/simulation surface. AI is an activity you write. Heavy server + Elasticsearch.

### Camunda 8 — BPMN / DMN process suite

**Pick when:** Enterprises standardized on BPMN 2.0, DMN, and Tasklist.

**Wins:** Industry notation, visual modeler, Tasklist, Operate, Keycloak, connectors.

**Loses vs the OS bar:** BPMN XML is a poor LLM authoring target. Cluster sprawl. One BPMN token graph, not dual-kernel. Copilot/connectors are not a legal-event autoCommit kernel.

### AWS Step Functions — managed cloud orchestration

**Pick when:** AWS-only shops gluing Lambda, SQS, DynamoDB, EventBridge with zero servers.

**Wins:** Zero infra, IAM, 200+ AWS integrations, pay-per-transition.

**Loses vs the OS bar:** AWS lock-in. ASL is one state machine, not Law+Work. Human wait is callback tokens. Bedrock in a state is not a tenant MCP DecisionPacket loop.

### Conductor / Orkes — microservice DAG orchestrator

**Pick when:** High-throughput worker DAGs (fork/join) in the Netflix/Orkes model.

**Wins:** Visual DAGs, multi-language workers, proven task queues.

**Loses vs the OS bar:** Polling workers. No decoupled FSM Law. No MCP. Inbox and policy are not first-class kernels.

### n8n / Zapier — iPaaS / departmental automation

**Pick when:** Departmental SaaS glue without a legal state machine.

**Wins:** Connector catalog, visual recipes, low training cost.

**Loses vs the OS bar:** Not a process OS. Weak isolation. AI nodes call HTTP; they do not restrict to legal nextSteps or auto-commit under Law.

## AI task automation

| Question | FlowOS | Temporal | Camunda 8 | Step Functions | Conductor | n8n / Zapier |
| --- | --- | --- | --- | --- | --- | --- |
| Who hosts decide → commit? | Native — DecisionPacket → tenant or FlowOS OpenAI → autoCommit or park | Custom — you write an activity | Custom — Copilot / connectors | Custom — Bedrock in a state | — | Custom — AI node, not legal nextSteps |
| Run without the tenant’s LLM key? | Yes on paid plan — `flowos-hosted`, daily cap (default 200) | Only if you wire a key in worker code | Vendor AI add-ons; not a tenant OS default | Uses the AWS account bill | — | Bring a key or n8n Cloud credits |
| Can the model invent a transition? | No — illegal nextSteps dropped; TimeoutEvent never auto-commits | Your code must reject it | Your process must reject it | Your state machine must reject it | Your worker must reject it | The recipe can fire any connected action |
| Design-time proof without live tokens? | `simulate_*` + AutoCommitEvaluator; no OpenAI call | Unit tests / time-skipping | Play / Operate, not a side-effect-free OS sim | Test executions still bill | Workflow test harness | Manual pin data; still a recipe runner |

## Honest later items (do not score as Native)

OIDC, Stripe checkout (`L-PAY`), OTEL, and SCIM remain later. Do not claim BPMN, Temporal-scale replay, or an AWS-sized connector catalog.
