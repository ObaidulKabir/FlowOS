# 21. Competitive comparison

This chapter is the durable OS-1 and MCP strategy scoreboard against the platforms buyers put next to FlowOS. The dashboard renders the same product comparison from `apps/dashboard/src/comparisonMatrix.ts`. Hosted-OpenAI policy stays in [Chapter 20](20-hosted-llm-and-automation-policy.md); the OS claim stays in [Chapter 19](19-os-release-gate.md).

**Evidence date:** 2026-09-21. Scores mean **Native / Custom / —** against FlowOS OS-1 pillars. They are architecture-fit judgments, not claims about market share, maturity, scale, or security certification.

## Executive conclusion

MCP support by itself is no longer a FlowOS moat. Temporal, Camunda, AWS, Orkes/Conductor, and n8n all have credible MCP products or officially supported servers.

The defensible FlowOS position is the complete governed chain:

> MCP authoring → validation → side-effect-free simulation → the same live decision policy → confirmation/idempotency → dual-kernel legal transition → durable audit and outcome metrics.

FlowOS should lead with **governed outcomes over MCP**, not “we have MCP” or “we have more tools.” The 74-tool registry is useful breadth and is contract-tested, but breadth becomes an advantage only when agents can select the right journey safely.

## Buy FlowOS when

The buyer needs tenant SaaS where AI agents author, simulate, and run **legal** work under separately versioned Law and Work, with HumanTask fallback, over MCP — including paid-plan hosted OpenAI so Agent/Either steps can run without a tenant LLM key.

## Buy someone else when

| If the buyer needs | Buy instead | Why |
| --- | --- | --- |
| Polyglot durable code + deterministic replay | Temporal.io | Workflows are programs and its durability ecosystem is substantially more mature. |
| BPMN 2.0 / DMN + analyst Tasklist | Camunda 8 | Camunda owns the notation and enterprise process-modeling category. |
| Zero-ops AWS service orchestration | AWS Step Functions | IAM and the AWS integration catalog are the product. |
| High-throughput microservice DAGs | Conductor / Orkes | Worker queues and fork/join DAGs are its core operating model. |
| Departmental SaaS recipes | n8n / Zapier | Connector speed and low-code usability beat a legal-state OS for this job. |

## OS-1 matrix

| Pillar | FlowOS | Temporal.io | Camunda 8 | AWS Step Functions | Conductor / Orkes | n8n / Zapier |
| --- | --- | --- | --- | --- | --- | --- |
| Identity | Native | Custom | Native | Native | Custom | Custom |
| Dual-kernel | Native | — | Custom | Custom | — | — |
| Policy | Native | Custom | Native | Custom | — | — |
| Inbox | Native | Custom | Native | Custom | Custom | Custom |
| Integrations | Native | Custom | Native | Native | Custom | Native |
| AI loop | Native | Custom | Custom | Custom | Custom | Custom |
| Simulation | Native | Custom | Custom | Custom | Custom | Custom |
| Ops | Native | Native | Native | Native | Native | Custom |
| Commercial | Native | Custom | Native | Native | Custom | Custom |

## MCP competitive reality

| Platform | Current MCP position | Strongest MCP capability | Gap relative to the FlowOS target |
| --- | --- | --- | --- |
| **FlowOS** | Built-in Streamable HTTP control plane; **74 contract-tested tools** across design, validation, publication, execution, simulation, agents, and administration | Lifecycle breadth plus tenant/capability checks, risk metadata, confirmation, idempotency, Law enforcement, and shared simulation/live agent policy | Does not yet expose every published workflow as its own dynamic MCP tool; OAuth/OIDC and broader client conformance evidence remain later |
| **Temporal** | Official Code Exchange operations server, documentation MCP, and first-party durable-MCP guidance | Workflow operations and using Temporal durability behind custom MCP tools | Workflow authoring stays code-first; legal-event policy, HumanTask OS, and agent evaluation are application code |
| **Camunda 8** | Built-in Orchestration Cluster MCP from 8.9; Processes MCP in 8.10 | Strong operations plus automatic process-as-tool exposure | One BPMN process model rather than separately pinned Law and Work; no shared FlowOS-style legal-event simulation/commit evaluator |
| **AWS Step Functions** | AWS Labs Step Functions Tool MCP Server and broader Serverless MCP tooling | IAM-governed, allowlisted state machines as tools; AWS resource creation/operations | AWS lock-in; ASL combines authority and work; bounded tenant agent policy and HumanTask fallback are custom |
| **Conductor / Orkes** | Conductor MCP server plus Orkes MCP Gateway | Create/operate workflows and map Gateway routes to workflows as tools | No separate Law kernel or native policy-parity agent simulation; decision governance remains worker/task code |
| **n8n** | Built-in instance MCP plus MCP Client, Client Tool, and Server Trigger nodes | Excellent bidirectional MCP and connector UX; selected workflows can be exposed to agents | No independent legal-state kernel, shared commit evaluator, or governed HumanTask fallback |

### What is actually differentiated

1. **Dual-kernel authority.** A model proposes an event; pinned Law determines whether that event can change state.
2. **Simulation/live parity.** `simulate_*` and live bounded-agent execution use the same legal-event and auto-commit evaluator without spending live tokens during simulation.
3. **Governed mutation contracts.** Tool metadata communicates risk and side effects; capability checks, confirmation, and idempotency protect writes.
4. **Durable bounded-agent runtime.** PostgreSQL jobs, leases, usage caps, provider-neutral attribution, strict output validation, and execution records cover the LLM decision itself.
5. **Outcome evidence.** Runs, commits, parks, failures, quota denials, overrides, tokens, latency, calibration, and matched outcomes are queryable.

### Where competitors are ahead

- **Camunda, Orkes, AWS, and n8n** have clearer workflow-as-an-MCP-tool publication paths. FlowOS currently offers catalog and start tools rather than one dynamically advertised tool per published business capability.
- **Temporal** is far ahead in durable-execution maturity, replay, SDK reach, and operational proof.
- **Camunda** is far ahead in BPMN/DMN modeling, analyst tooling, and enterprise process adoption.
- **AWS** is far ahead in managed operations, IAM reach, and native service integrations.
- **n8n** is far ahead in connector breadth and low-friction departmental automation.
- **FlowOS** still needs OIDC/OAuth-based MCP access, OTEL, compatibility testing across major MCP clients, and production-scale evidence.

## MCP priorities

| Priority | Investment | Required outcome |
| --- | --- | --- |
| P0 | Contract truth | Generate discovery count, schemas, examples, errors, and deprecations from one registry; prevent dashboard/docs drift. |
| P0 | Simulation → execution proof | Present dry-run, decision diff, confirmation, idempotency, legal commit, and audit as one visible safety chain. |
| P1 | Guided tool packs | Group 74 tools into design, publish, operate, incident, and agent journeys so clients choose outcomes rather than isolated endpoints. |
| P1 | Measured reliability | Publish task success, invalid-call rate, p95 latency, overrides, and outcome-match rate by MCP client and release. |
| P1 | Workflow-as-tool publication | Safely advertise selected published WorkflowClasses as narrowly scoped MCP tools without bypassing Law or tenant policy. |

## Per platform

### FlowOS — business automation OS

**Wins:** Declarative JSON, broad built-in MCP lifecycle, fail-closed dual-kernel Law, hosted/BYO DecisionPacket loop, deterministic simulation, HumanTask fallback, and a compact .NET 8 + PostgreSQL footprint.

**Loses:** Not BPMN. Not Temporal/Zeebe scale. No AWS-sized connector catalog. Dynamic workflow-as-tool publication, OIDC, Stripe checkout, OTEL, and SCIM are later.

### Temporal.io — durable execution

**Wins:** Durable execution, deterministic replay, multi-language SDKs, retries, scale, and credible MCP operations/durable-tool patterns.

**FlowOS distinction:** FlowOS supplies tenant Law, HumanTask inbox, bounded decision policy, simulation parity, and hosted-agent governance as product kernels rather than application activities.

### Camunda 8 — BPMN / DMN process suite

**Wins:** Industry notation, modeler, Tasklist, Operate, connectors, built-in cluster MCP, and process-as-tool publication. It is the strongest OS-shaped MCP competitor.

**FlowOS distinction:** FlowOS uses LLM-friendly JSON and keeps legal authority separately versioned and pinned from the work graph.

### AWS Step Functions — managed cloud orchestration

**Wins:** Zero infrastructure, IAM, AWS integrations, console visualization, and allowlisted state machines exposed through AWS-supported MCP tooling.

**FlowOS distinction:** FlowOS is cloud-neutral and packages tenant policy, inbox, deterministic bounded-agent simulation, and legal-event commits together.

### Conductor / Orkes — microservice DAG orchestrator

**Wins:** Visual DAGs, multi-language workers, task queues, workflow lifecycle MCP, and MCP Gateway routes.

**FlowOS distinction:** FlowOS adds separately enforced Law, HumanTask fallback, shared live/simulation agent policy, and hosted-provider quota/audit.

### n8n / Zapier — iPaaS / departmental automation

**Wins:** Connector catalog, visual recipes, low training cost, instance-level MCP, and bidirectional MCP nodes.

**FlowOS distinction:** FlowOS targets governed tenant processes where an agent suggestion must remain inside legal next events and may park for a human.

## AI task automation

| Question | FlowOS | Temporal | Camunda 8 | Step Functions | Conductor | n8n / Zapier |
| --- | --- | --- | --- | --- | --- | --- |
| Who hosts decide → commit? | Native DecisionPacket → tenant or FlowOS OpenAI → commit or park | Custom activity/workflow code | Custom process/connector logic | Custom Bedrock/service state | Custom AI/task worker | Custom AI Agent node |
| Run without the tenant’s LLM key? | Yes on paid plan; `flowos-hosted` has a daily cap | Provider credentials belong in the application/worker | Depends on configured vendor/provider services | Uses the AWS account | Depends on configured model/task provider | Bring a key or use applicable cloud credits |
| Can the model invent a legal transition? | No; suggestions outside pinned next events are rejected and timer events never auto-commit | Application code must reject it | Process logic must reject it | State machine must reject it | Worker/workflow must reject it | Connected workflow actions define the boundary |
| Design proof without live tokens? | `simulate_*` plus the same `AutoCommitEvaluator`; no provider call | Unit tests/time skipping | Play/test tooling; not the same bounded-agent evaluator | State-machine tests; not shared agent-policy simulation | Workflow test harness | Pinned/manual test data |

## Claims not to make

- Do not say competitors lack MCP. That statement is now false.
- Do not present 74 tools as the moat; present the governed lifecycle and evidence.
- Do not claim generic simulation is unique. The distinction is simulation/live **policy parity under pinned Law**.
- Do not claim dynamic workflow-as-tool publication yet; several competitors are ahead here.
- Do not claim BPMN, Temporal-scale replay, an AWS-sized integration catalog, OIDC, OTEL, Stripe checkout, or SCIM.

## Official sources

- Temporal: [Temporal MCP Server](https://temporal.io/code-exchange/temporal-mcp-server) and [durable MCP guidance](https://docs.temporal.io/ai/cookbook/hello-world-durable-mcp-server)
- Camunda: [Orchestration Cluster MCP](https://docs.camunda.io/docs/next/apis-tools/orchestration-cluster-api-mcp/orchestration-cluster-api-mcp-overview/) and [Processes MCP](https://docs.camunda.io/docs/next/apis-tools/processes-mcp/processes-mcp-overview/)
- AWS: [Step Functions Tool MCP Server](https://awslabs.github.io/mcp/servers/stepfunctions-tool-mcp-server)
- Orkes: [Conductor MCP announcement](https://orkes.io/blog/conductor-mcp-server-announcement) and [MCP Gateway](https://orkes.io/content/developer-guides/mcp-gateway)
- n8n: [instance-level MCP](https://docs.n8n.io/connect/connect-to-n8n-mcp-server) and [MCP Client](https://docs.n8n.io/integrations/builtin/core-nodes/n8n-nodes-langchain.mcpclient/)
