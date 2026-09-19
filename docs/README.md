# FlowOS Documentation

Welcome to the FlowOS user guide. FlowOS is a **business automation operating system**: tenant work runs under dual-kernel Law (state machines) and Work (workflows), gated by capabilities and deny-only policy, with a HumanTask inbox, capability-bound integrations, a hosted DecisionPacket / autoCommit loop, side-effect-free simulation, and paid runtime entitlement. The claim is gated by [Chapter 19](19-os-release-gate.md).

This guide is organized as a linear path — read it in order if you are new, or jump directly to the chapter you need. Examples are maintained alongside the current codebase and automated test suite.

## How the guide is organized

| # | Chapter | What you'll learn |
|---|---|---|
| 1 | [Getting Started](01-getting-started.md) | Install, run locally or via Docker, make your first API call |
| 2 | [Core Concepts](02-core-concepts.md) | The Law/Work/Truth mental model, invariants, multi-tenancy |
| 3 | [State Machines (The Law)](03-state-machines.md) | Defining legal states/transitions, validating transitions via API |
| 4 | [Events & the Event Registry (The Truth)](04-events-and-registry.md) | Registering events, naming conventions, publishing events with payloads |
| 5 | [Workflows & Versioning (The Work)](05-workflows-and-versioning.md) | Authoring workflows in JSON/C#, starting instances, semantic versioning |
| 6 | [Human Tasks & Decisions](06-human-tasks-and-decisions.md) | Completing tasks, branching via events, data-driven Decision steps |
| 7 | [AI Agents & Insights](07-ai-agents-and-insights.md) | Advisory agents, Suggested Actions, human-in-the-loop confirmation |
| 8 | [Security: Roles, Capabilities & Policies](08-security-roles-and-policies.md) | RBAC, `[RequiresCapability]`, dynamic deny-only policies |
| 9 | [WorkflowClass Governance](09-workflow-class-governance.md) | Authoring blueprints, Draft → Published → Shared → Public lifecycle |
| 10 | [Notifications](10-notifications.md) | Real-time SSE notifications, failure isolation, idempotency |
| 11 | [Recovery & Resilience](11-recovery-and-resilience.md) | Crash recovery, transactional guarantees |
| 12 | [Anti-Patterns](12-anti-patterns.md) | Common mistakes and how to avoid them |
| 13 | [MCP & AI Agent Automation](13-mcp-and-ai-agent-integration.md) | The Model Context Protocol server, tool reference, governance constitution |
| 14 | [API Reference](14-api-reference.md) | Every REST endpoint, request/response shapes |
| 15 | [Known Limitations & Gaps](15-known-limitations-and-gaps.md) | Honest list of enforcement gaps proven by regression tests |
| 16 | [Sample Applications](16-sample-applications.md) | ExpenseApp, the Tenant Dashboard, and the Node.js demo client |
| 17 | [Workflow Context Bindings](17-workflow-context-bindings.md) | Reuse and simulate one process template across tenant business contexts |
| 18 | [Commercial Policy & MCP Entitlements](18-commercial-and-mcp-entitlements.md) | Subscription plans, trial vs paid runtime, and how MCP is included |
| 19 | [OS-1 Honesty Gate](19-os-release-gate.md) | When FlowOS may be called a business automation OS vs a workflow engine |
| 20 | [Hosted LLM & Automation Policy](20-hosted-llm-and-automation-policy.md) | Paid-plan FlowOS OpenAI default, daily quota, BYO opt-in, host key rules |

## Reading paths

* **"I want to run it and try the API"** → Start with [Getting Started](01-getting-started.md), then [API Reference](14-api-reference.md).
* **"I want to design a business process"** → [Core Concepts](02-core-concepts.md) → [State Machines](03-state-machines.md) → [Events](04-events-and-registry.md) → [Workflows](05-workflows-and-versioning.md) → [WorkflowClass Governance](09-workflow-class-governance.md).
* **"I'm building a client application"** → [Human Tasks & Decisions](06-human-tasks-and-decisions.md) → [Notifications](10-notifications.md) → [Anti-Patterns](12-anti-patterns.md) → [Sample Applications](16-sample-applications.md).
* **"I am integrating an AI agent"** → [AI Agents & Insights](07-ai-agents-and-insights.md) → [MCP & AI Agent Automation](13-mcp-and-ai-agent-integration.md) → [Commercial Policy](18-commercial-and-mcp-entitlements.md) → [Hosted LLM & Automation Policy](20-hosted-llm-and-automation-policy.md).
* **"How do paying tenants get AI automation without their own API key?"** → [Hosted LLM & Automation Policy](20-hosted-llm-and-automation-policy.md).
* **"I want one workflow template for many business domains"** → [WorkflowClass Governance](09-workflow-class-governance.md) → [Workflow Context Bindings](17-workflow-context-bindings.md).
* **"I need to know exactly what is and isn't enforced today"** → [Known Limitations & Gaps](15-known-limitations-and-gaps.md).
* **"Can we call this a business automation OS?"** → [OS-1 Honesty Gate](19-os-release-gate.md) (MCP: `flowos://guides/os-release-gate`).

## Documentation conventions

* All curl examples assume the API is running locally via `dotnet run` on `http://localhost:5183` (see [Getting Started](01-getting-started.md) for why this is the correct port, not 5000/5001/5005 as older docs claimed).
* `x-tenant-id` and `X-Mock-Role` are development-only headers provided by `MockAuthMiddleware`. They are **not** present in a production authentication setup.
* Every code snippet is either copied from, or directly modeled on, a passing automated test. Where a file path is cited (e.g. *Derived from: `tests/FlowOS.EndToEndTests/...`*), you can open that file to see the exact assertions.

## Source of truth

This guide replaces the following, previously scattered, documentation locations, which have been consolidated and deleted:

* Root: `API_DOCUMENTATION.md`, `EVENT_REGISTRY_GUIDE.md`, `NOTIFICATION_SERVICE_TEST.md`
* `docs/`: `UserManual.md`, `PayloadEvaluation.md`, `AgentIntegrationStrategy.md`, `FlowOS_AI_Integration_Spec.md`, `FlowOS Design MCP Support.md`, `MCP_Usage_Examples.md`, `MCP_Review_And_Gap_Analysis.md`, `InvariantManifest.md`, `WorkflowClass_Definition.md`, `WorkflowClass_API_Curl_Guide.md`, `Role_Capability_Guide.md`, `Policy_Guide.md`, `Workflow_Versioning_Guide.md`, `Workflow_API_Curl_Guide.md`, `Event_API_Curl_Guide.md`, `StateMachine_API_Curl_Guide.md`, `Human_AI_Interaction_Curl_Guide.md`, `Notifications_System_Guide.md`
* `docs/governance/workflow-class.md`
* `docs/sdk/01-mental-model.md` through `docs/sdk/11-workflow-class-authoring.md`

`Workflow_API_Test_Report.md` (repo root) is left untouched — it is a point-in-time QA report, not user-facing documentation.
