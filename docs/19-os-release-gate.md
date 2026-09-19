# 19. OS-1 Honesty Gate

This chapter is the **release gate** that decides whether FlowOS may be called a **business automation operating system**, or only a **workflow engine** with extra surfaces.

A workflow engine starts graphs, advances steps, and maybe waits on timers. A business automation OS owns the business: who is acting, what is legal, whose work it is, which systems may be touched, how AI may decide, how designs are proven without side effects, how the fleet is operated, and who has paid the right to run.

Agents must load MCP prompt `check_os_release_gate` or resource `flowos://guides/os-release-gate` before using the OS claim. Humans use this page.

## Machine contract

Do not edit `VERDICT` or `CLAIM_ALLOWED` unless every `must-pass` row below is `done`. The MCP contract test fails if those disagree.

```
GATE=OS-1 Honesty Gate
VERDICT=GREEN
CLAIM_ALLOWED=true
OS-ID=done
OS-KERNEL=done
OS-LAW=done
OS-INBOX=done
OS-INT=done
OS-AI=done
OS-SIM=done
OS-OPS=done
OS-COMM=done
```

## Allowed product claims

| Gate | Sentence you may use |
|---|---|
| **GREEN** (today) | FlowOS is a business automation operating system: tenant work runs under dual-kernel Law (state machine) and Work (workflow), gated by capabilities and deny-only policy, with a HumanTask inbox, capability-bound integrations, a hosted DecisionPacket / `autoCommit` loop, side-effect-free simulation, and paid runtime entitlement. |
| **RED** | FlowOS is a dual-kernel process engine with an MCP control plane — not yet a business automation operating system. |

If `VERDICT` is not `GREEN`, the GREEN sentence is a false claim. Use the RED sentence, or name the specific kernels that exist (dual-kernel, context bindings, MCP, bounded autonomy) without calling the product an OS.

## Engine vs OS

| Pillar | Workflow engine | Business automation OS (FlowOS bar) |
|---|---|---|
| Identity / tenancy | Optional header or single DB | Tenant identity, isolation, anti-enumeration. Production callers cannot spoof `x-tenant-id`. |
| Process kernel | Step graph is authority | Dual-kernel: state machine is Law, workflow is Work. No live `publish_event` path advances Work when Law is missing or would deny. |
| Policy / law | Happy-path RBAC | Capabilities, deny-only policies, and human confirmation actually gate publish / activate / irreversible MCP tools. |
| Work inbox | Instance list | Role-relevant HumanTasks, SLA reminders, Smart Actions; humans complete work, they do not pick the next step. |
| Integrations | Webhook afterthought | Tenant capability bindings and resource plugins (`LookupRecord` / `QueryRecords` / `FetchDocument` / `SearchKnowledge` / `CheckPolicy`). Models never see URLs. |
| AI decision loop | Chat that calls `publish_event` | Hosted wait → DecisionPacket (Prompt/Data/Tools/Provider) → `IWorkflowAgent` → `autoCommit` or park. TimeoutEvent stays timer-owned. |
| Simulation | Manual staging tenant | `simulate_workflowclass` / `simulate_context_binding` with dual-kernel catch-up and SLA paths, no Outbox / persist. |
| Operations | Process uptime | Crash resume, DLQ, time-travel, action history, dual live hosts, health. |
| Commercial | Open runtime | Trial may design/simulate; runtime returns `MCP-PLAN-REQUIRED` until Managed/Enterprise `BillingStatus=Active`. |

## Must-pass criteria

Each row is **must-pass**. The gate stays RED while any status is `partial` or `missing`. All must-pass rows below are `done`.

### OS-ID — Identity and tenancy

| | |
|---|---|
| **Capability** | A caller is a tenant (and a user or API key). Rows and MCP tools are tenant-scoped. Foreign IDs return `MCP-NOTFOUND-001`. |
| **Status** | **done** |
| **Proof** | `POST /api/auth/register-tenant`, `login` JWT, `TenantApiKey`, MCP host-scoped keys; credential tenant wins over `x-tenant-id`; `FlowOS:Identity:AllowMockAuth` defaults off outside Development; `tests/FlowOS.UnitTests/Security/TenantIdentityTests.cs`; `tests/FlowOS.UnitTests/Integration/IdentityGateTests.cs`; `tests/FlowOS.EndToEndTests/Security/Tenant_Registration_And_Auth_E2E_Tests.cs`; `tests/FlowOS.MCP.UnitTests/ContractAndTenantTests.cs`. |
| **Engine blocker** | None for v1. Mock Admin remains Development-only. Invite/SCIM is later (`L-USERS`). |

**Done when:** Production/Staging reject unauthenticated API/MCP calls; tenant comes from JWT or key, not a spoofable header; object-level isolation tests still pass.

### OS-KERNEL — Dual-kernel process kernel

| | |
|---|---|
| **Capability** | `currentStep` (Work) and `currentState` (Law) move independently. `PublishEventCommand` consults a pinned or class state machine. Decision `Default` does not consume a business event; unused SM events apply as state-only catch-up. |
| **Status** | **done** |
| **Proof** | `WorkflowEngine.Advance` + `WorkflowStateEnforcementTests`; class-backed `publish_event` / `complete_task` fail closed without usable Law (`EnsureClassBackedLaw`); inverted `WorkflowCommandHandlers_StateMachineGapTests`. MCP: `flowos://guides/dual-kernel-design`. |
| **Engine blocker** | None for v1. Static Law at publish remains later (`L-LAW-STATIC`). |

**Done when:** Live `publish_event` / `complete_task` / auto-commit **fail closed** without a resolvable SM for class-backed instances, and the gap test is inverted (deny, not advance). Static Law at publish may stay later (`L-LAW-STATIC`).

### OS-LAW — Policy and capability law

| | |
|---|---|
| **Capability** | `[RequiresCapability]` / `PolicyEnforcementBehavior` can deny a legal graph move. MCP `publish_workflowclass`, `activate_context_binding`, `archive_context_binding` require `confirmHumanApproval: true`. |
| **Status** | **done** |
| **Proof** | [Chapter 8](08-security-roles-and-policies.md); `ApproveWorkflowClassCommand` is Admin/SuperAdmin plus `workflow.approve_public`; `DefaultPolicyEvaluator` fail-closed on malformed JSON; `tests/FlowOS.UnitTests/Application/Handlers/ApproveWorkflowClassAdminOnlyTests.cs`; `tests/FlowOS.UnitTests/Security/PolicyEvaluatorGapTests.cs`; MCP `MCP-APPROVAL-REQUIRED`. |
| **Engine blocker** | None for v1. Richer case-data `ConditionJson` is later (`L-POLICY`). |

**Done when:** Promote-to-Public is admin-only; production identity is real (see OS-ID); deny policies still fail closed on malformed JSON.

### OS-INBOX — Human work inbox and SLA

| | |
|---|---|
| **Capability** | Waiting HumanTask/Command is the unit of work. Humans see **their** tasks, SLA reminders, parked Smart Actions. Completing a task publishes an event; the client does not choose `currentStep`. TimeoutEvent is timer-owned. |
| **Status** | **done** |
| **Proof** | `GET /api/tasks` filters by caller roles / step `AllowedRoles`; insights hang off the task; `AutoCommitEvaluator` rejects TimeoutEvent; `tests/FlowOS.UnitTests/Integration/TaskApiTests.cs`; `tests/FlowOS.UnitTests/Agents/BoundedAutonomyTests.cs`; SLA simulate guide `flowos://guides/sla-reminder-simulation`. |
| **Engine blocker** | None for v1. Dashboard Inbox tab + MCP `list_tasks` is later (`L-INBOX-UX`). |

**Done when:** Task list is filtered by the caller’s roles (context-binding overrides included), Smart Actions/insights hang off that task, and SLA overdue cannot be auto-committed.

### OS-INT — Integrations and capability bindings

| | |
|---|---|
| **Capability** | Tenants bind real systems. Step actions and agent resource plugins invoke through bindings. Remote HTTP is flag-gated. Models never receive endpoint URLs or API keys. |
| **Status** | **done** |
| **Proof** | MCP `register_capability_binding`, `register_plugin_binding`, `list_registered_plugins`, `test_action_plugin`, webhook HMAC tools; `LookupRecordResourcePlugin` and siblings; `FlowOS:Capabilities:EnableRemoteInvoke`. |
| **Engine blocker** | None for v1. Unwired remote invoke is a safe default, not a missing kernel. |

### OS-AI — Bounded-autonomy DecisionPacket loop

| | |
|---|---|
| **Capability** | Waiting step `actor` Agent/Either. FlowOS hosts wait → DecisionPacket (Prompt/Data/Tools/redacted Provider) → agent → `autoCommit` subset of `nextSteps` or park. External chat must not free-form `publish_event`. |
| **Status** | **done** |
| **Proof** | [Chapter 7](07-ai-agents-and-insights.md); `WorkflowAgentFactory` + `TenantLlmWorkflowAgent` (secrets loaded internally); `flowos-risk` still uses `RiskAnalysisAgent`; MCP `run_agent_task` / `get_agent_context`; `tests/FlowOS.UnitTests/Agents/TenantLlmWorkflowAgentTests.cs`; `tests/FlowOS.UnitTests/Agents/BoundedAutonomyTests.cs`. |
| **Engine blocker** | None for v1. |

**Done when:** A registered `agent` plugin binding is what `run_agent_task` executes (or an explicit `flowos-risk` provider), keys never enter Agent Context, and illegal suggestions are still dropped.

### OS-SIM — Context-aware simulation

| | |
|---|---|
| **Capability** | Prove dual-kernel, context bindings, and SLA reminder vs timeout without `start_workflow` and without Outbox side effects. Draft bindings may simulate; activate still needs a Published source. WorkflowClass roles stay on BusinessRoles and never write tenant Role rows. |
| **Status** | **done** |
| **Proof** | `simulate_workflowclass`, `simulate_context_binding`, `fork_workflow_simulation`; MCP guides `dual-kernel-design` and `sla-reminder-simulation`; `tests/FlowOS.MCP.UnitTests/HttpIntegrationTests.cs`. |
| **Engine blocker** | None for v1. |

### OS-OPS — Operations and dual-host control plane

| | |
|---|---|
| **Capability** | Crash-safe resume, dead letters, time-travel, action history, `/health/live` + `/health/ready`, two live MCP hosts with advertised JSON-RPC URLs. |
| **Status** | **done** |
| **Proof** | [Chapter 11](11-recovery-and-resilience.md); MCP `list_dead_letters`, `replay_workflow_history`, `get_instance_action_history`; `tests/FlowOS.UnitTests/Infrastructure/FlowOsPublicUrlsTests.cs` (`flowosbd.com` `/mcp/` vs staging `/mcp`). |
| **Engine blocker** | None for v1. OpenTelemetry is later (`L-OTEL`). |

### OS-COMM — Commercial entitlement

| | |
|---|---|
| **Capability** | MCP is included in the tenant subscription. Trial may discover/lint/validate/simulate. Runtime tools and REST `[RequireRuntimePlan]` return `MCP-PLAN-REQUIRED` (HTTP 402) until Managed/Enterprise + `BillingStatus=Active`. |
| **Status** | **done** |
| **Proof** | [Chapter 18](18-commercial-and-mcp-entitlements.md); `ITenantEntitlementService`; `tests/FlowOS.UnitTests/Infrastructure/TenantEntitlementServiceTests.cs`; `tests/FlowOS.MCP.UnitTests/EntitlementHttpTests.cs`. Admin `POST /api/admin/tenants/{id}/plan`. |
| **Engine blocker** | None for v1. A payment provider is later (`L-PAY`). Entitlement without checkout is still an OS kernel. |

## Later (does not block GREEN)

| ID | Item | Why it can wait |
|---|---|---|
| `L-PAY` | Stripe (or other) checkout | Admin-assigned plans already gate runtime. |
| `L-SSO` | OIDC / enterprise IdP | First-party JWT + API keys can satisfy OS-ID if spoofing is gone. |
| `L-USERS` | Invite / SCIM | OS-ID done-when does not require a full directory. |
| `L-OTEL` | Metrics and distributed tracing | Health, DLQ, and replay are the ops kernel. |
| `L-LAW-STATIC` | Publish-time workflow-vs-SM projection | Runtime fail-closed (OS-KERNEL) is the OS bar. |
| `L-POLICY` | Richer `ConditionJson` language | Deny-only JSON is enough if identity is real. |
| `L-INBOX-UX` | Dashboard Inbox tab + MCP `list_tasks` | Follows OS-INBOX role filtering; not a substitute for it. |

## How the gate stays GREEN

1. Keep every must-pass row `done` here **and** in `FlowOsMcpGuidance.OsReleaseGateGuide`.
2. Do not set `VERDICT=RED` unless an engine blocker returns. `OsReleaseGateTests` requires GREEN ⇒ every must-pass is `done`.
3. Later items (`L-PAY`, `L-SSO`, `L-USERS`, `L-OTEL`, `L-LAW-STATIC`, `L-POLICY`, `L-INBOX-UX`) do not reopen the gate.

README, landing copy, and agents may use the GREEN sentence while this contract stays GREEN.
