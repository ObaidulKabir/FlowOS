# 13. MCP & AI Agent Control Plane

FlowOS is a **multi-tenant, state-machine-governed workflow control plane with an MCP interface for safe AI-agent interaction**.

Unlike standard MCP servers that merely expose thin CRUD wrappers, FlowOS functions as an authoritative control plane. Agents can reason, design, lint, execute, and inspect workflows, while FlowOS enforces strict tenant boundaries, formal schema validation, state-machine invariants, side-effect classifications, and mandatory human approval gates for irreversible fleet operations.

## Architecture: Discovery & Execution Separation

```
GET  advertised path  ──► Public Discovery (HTML / JSON metadata, schemas, risk levels, human confirmation)
POST advertised path  ──► Authenticated JSON-RPC 2.0 (tenant resolution, object-level IDOR check, human gates)
```

Agents must POST JSON-RPC to the **advertised `url` exactly**. Do not strip a trailing slash. Do not follow 301/302 for POST.

| Host | JSON-RPC path | Absolute URL |
|------|---------------|--------------|
| `https://flowosbd.com` | `/mcp/` (trailing slash required) | `https://flowosbd.com/mcp/` |
| `https://flowos.prospectbdltd.com` | `/mcp` | `https://flowos.prospectbdltd.com/mcp` |
| localhost | `/mcp` | `http://localhost:8080/mcp` |

On production, nginx currently 301s `POST /mcp` to `http://flowosbd.com/mcp/`, which drops the body and API key. Discovery JSON (`GET /mcp`, `GET /.well-known/mcp`) therefore publishes `url` / `connection.jsonrpcUrl` / `connection.jsonrpcPath` for the current host. `initialize` instructions repeat the same contract. Tenant API keys are host-scoped.

A current MCP client (Trae, Cursor, Claude) can use that URL directly:

```json
{
  "mcpServers": {
    "flowos": {
      "url": "https://flowosbd.com/mcp/",
      "headers": {
        "X-MCP-API-Key": "YOUR_TENANT_API_KEY",
        "x-tenant-id": "YOUR_TENANT_ID"
      }
    }
  }
}
```

`initialize` may send a newer `protocolVersion` such as `2025-11-25`. FlowOS answers with the newest version it implements (`2025-03-26`, or `2024-11-05` when the client asked for that version). It does not fail the handshake. Later requests may omit `MCP-Protocol-Version` or repeat the client's newer dated version. `Accept` may be `application/json`, `text/event-stream`, or both. `x-tenant-id` may be omitted when the API key or login JWT already belongs to one tenant.

## Running the MCP server

### Stdio (local IDE / Cursor agent)

```bash
dotnet run --project src/FlowOS.MCP/FlowOS.MCP.csproj
```

Communicates over **stdio** (`stdin`/`stdout`) using JSON-RPC 2.0. Logging goes to stderr/Debug so it never pollutes the JSON-RPC stream on stdout.

### Streamable HTTP

```bash
# PowerShell (Development / Sandbox Mode)
$env:MCP_TRANSPORT="http"
$env:ASPNETCORE_URLS="http://0.0.0.0:8080"
$env:MCP_API_KEY="disabled"  # Set to "disabled" or omit for key-free sandbox testing
$env:MCP_ROLE="Admin"
dotnet run --project src/FlowOS.MCP/FlowOS.MCP.csproj
```

```bash
# bash (Production Mode with API Key)
MCP_TRANSPORT=http ASPNETCORE_URLS=http://0.0.0.0:8080 \
MCP_API_KEY=replace-with-a-long-random-secret MCP_ROLE=Admin \
  dotnet run --project src/FlowOS.MCP/FlowOS.MCP.csproj
```

Endpoints:

| Method | Path | Behavior |
|--------|------|----------|
| `GET` | `/mcp` or `/mcp/` | **Public Discovery**: Returns interactive HTML documentation (Accept: `text/html`) or machine-readable JSON metadata (Accept: `application/json`) including all tool schemas, risk levels, side effects, confirmation requirements, and a `connection` object with the exact JSON-RPC URL. No credentials required. |
| `POST` | advertised `url` (`/mcp` on staging/local, `/mcp/` on flowosbd.com) | **Protected Execution**: Authenticated JSON-RPC 2.0 body (`initialize`, `tools/list`, `tools/call`). Requires `x-tenant-id` and API key/bearer. Do not follow POST redirects. |
| `OPTIONS`| `/mcp` or `/mcp/` | CORS preflight handling for web/browser agent environments. |
| `GET` | `/health` | `200` `{ "status": "ok" }` |

> **Commercial policy:** MCP is included on every package. The published ladder is Free → Starter ($9) → Builder ($29) → Team ($79) → Growth ($199) → Scale ($499) → Enterprise (custom), metered on publications, events, and generous MCP calls. Register without activation is Free (design-time). Runtime tools (`start_workflow`, `publish_event`, `complete_task`, publish/activate) return `MCP-PLAN-REQUIRED` until a platform Admin activates a paid plan (`Managed` or `Enterprise`, `BillingStatus=Active`). See [Chapter 18](18-commercial-and-mcp-entitlements.md).
>
> **Sandbox mode:** Setting `MCP_API_KEY=disabled` (or leaving `MCP_API_KEY` unconfigured in Development) bypasses the `X-MCP-API-Key` header requirement and billing enforcement for local evaluation. Every authenticated request still requires a valid `x-tenant-id` when keys are enabled.

Smoke test (Sandbox Mode):

```bash
curl -s http://localhost:8080/health

curl -s -X POST http://localhost:8080/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}}}'

curl -s -X POST http://localhost:8080/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -H "MCP-Protocol-Version: 2025-03-26" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
```

### Temporary In-Memory Sandbox Docker Compose
For testing and learning without setting up PostgreSQL or API keys:
```bash
docker compose -f docker-compose.sandbox.yml up --build
```
- **API**: `http://localhost:5183` (In-Memory Database)
- **MCP**: `http://localhost:8081` (Unauthenticated Sandbox Mode)

Cursor remote MCP example (`mcp.json`):

```json
{
  "mcpServers": {
    "flowos": {
      "url": "http://localhost:8081/mcp",
      "headers": {
        "x-tenant-id": "11111111-1111-1111-1111-111111111111"
      }
    }
  }
}
```

If no `ConnectionStrings:DefaultConnection` is configured, MCP falls back to an in-memory database (`FlowOS_MCP_Db`) — separate from the API's own in-memory instance.

## Governance constitution summary

FlowOS treats AI as **a designer, a reasoner, a proposer** — never an executor, a decision authority, or a governance bypass.

* **Agents MAY**: propose new `WorkflowClass` Drafts, modify Draft blueprints, request authoritative validation, interpret validation errors, iterate designs, propose notification/policy mappings, inspect read-only runtime context, propose `SuggestedAction`s ([Chapter 7](07-ai-agents-and-insights.md)).
* **Agents MAY NOT**: execute workflows or steps, publish WorkflowClasses, advance instances, emit domain events, modify runtime data, bypass validation, or access tenant operational data without authorization.
* **MCP is the sole interaction surface** for agents — no direct API/database access, no hidden capabilities. If a capability isn't exposed as an MCP tool, it is out of bounds.
* **Validation success never implies authority to act.** A valid Draft is still not executable and still not authoritative until an explicit `Publish` by an authorized actor.
* **Fail-closed on uncertainty.** If an agent is uncertain about rule interpretation, structure, or event semantics, it must treat the design as INVALID and ask, rather than guess.

## Governance principles vs. actual validator codes — read this before trusting either table

Earlier internal design documents for this project described an aspirational rule-ID scheme (`SM-001`, `WF-002`, `EV-001`, `GOV-001`...) as if it were the live validator's vocabulary. **It is not.** The principles below are still valid *intent*, but the codes a WorkflowClass draft actually fails with come from `WorkflowClassValidator.Validate` and are documented, verified, in [Chapter 9](09-workflow-class-governance.md#validation-rules--verified-against-workflowclassvalidatorvalidate-the-actual-error-codes-it-emits) — a **different, disjoint code vocabulary** (`STR-*`, `WF-STR-*`, `CON-*`, `WF-COMP-*`, `WF-STRUCT-005`, `WF-VAL-*`, `EVT-SCHEMA-001`, `GOV-001`).

**Principles (intent, not literal error codes):**

* A StateMachine has exactly one `InitialState`; every transition references defined states/events; states should be reachable; no implicit/inferred transitions.
* A Workflow has exactly one `StartStepId`; every step should be reachable; progression is event-driven, not manual/time-based; a step shouldn't dead-end silently.
* Events are explicit, immutable once published, fact-only (never commands).
* Only Drafts may be mutated; Published designs are immutable; validity never implies publish/execution authority.

**When citing a rejected design to a human or another agent, use the real codes from [Chapter 9](09-workflow-class-governance.md#validation-rules--verified-against-workflowclassvalidatorvalidate-the-actual-error-codes-it-emits), not the principle names above** — the principle names don't appear anywhere in an actual `ValidateOnly`/`Publish` API response.

## MCP tool reference — verified against `ToolRegistration.RegisterAll()`

`tools/list` is self-describing: every tool advertises its canonical input
schema plus behavioral constraints, result shape, stable error codes, and a
compact JSON input example. Successful tool content uses
`{ "ok": true, "data": ... }`; tool-level failures set `isError: true` and
return `{ "ok": false, "errorCode": "...", "message": "...", "context": ... }`.

FlowOS advertises **113 tool names**: 110 canonical tools plus 3 deprecated
capability-binding aliases retained for compatibility. They cover governance
lifecycle, context-aware simulation, Copilot synthesis, time-travel debugging,
operational execution, tenant IAM, connector and plugin registry, resilience/DLQ,
webhook security, multi-turn agent chat, external AI change feed, reliability primitives,
and runtime advisory intelligence:

| Tool name | Risk Level | Side Effect | Requires Human Confirmation | Implementation | Description |
|---|---|---|---|---|---|
| `describe_workflowclass_schema` | `low` | `none` | No | `InfoTools.DescribeSchema` | Returns JSON schema aligned with `WorkflowClassBlueprint` (`eventId`, roles/capabilities, real `stepTypes`). |
| `list_public_workflowclasses` | `low` | `none` | No | `InfoTools.ListPublic` | Lists `{ id, name, version }` for every `Public`-scope WorkflowClass blueprint. |
| `get_plan_status` | `low` | `none` | No | `PlanActivationMcpTools.GetPlanStatus` | Shows plan, billing status, runtime eligibility and the state of the activation request. Works while the plan is blocked. |
| `request_plan_activation` | `low` | `reversible` | No | `PlanActivationMcpTools.RequestPlanActivation` | Files a plan activation request for admin approval; idempotent; never activates the plan. Works while the plan is blocked. |
| `list_notifications` | `low` | `none` | No | `NotificationTools.ListNotifications` | Lists recent tenant and user notifications with severity levels. |
| `mark_notification_as_read` | `low` | `reversible` | No | `NotificationTools.MarkNotificationAsRead` | Marks a specific notification as read. |
| `list_available_agents` | `low` | `none` | No | `AgentTools.ListAvailableAgents` | Lists registered runtime agents (e.g. `RiskAnalysisAgent`) and their capabilities. |
| `suggest_agent_action` | `low` | `none` | No | `AgentTools.SuggestAgentAction` | Builds a DecisionPacket and returns legal `SuggestedAction`s without publishing. |
| `run_agent_task` | `medium` | `irreversible` | No | `AgentTools.RunAgentTask` | Hosts wait → packet → agent → auto-commit or park. Does not take an arbitrary event. |
| `get_agent_context` | `low` | `none` | No | `AgentContextMcpTools.GetAgentContext` | Composes live Agent Context (Prompt + Data + Tools + Provider) without running the agent. |
| `preview_agent_context` | `low` | `none` | No | `AgentContextMcpTools.PreviewAgentContext` | Design-time Agent Context for a workflow class step. Does not start an instance. |
| `upsert_agent_prompt` | `low` | `reversible` | No | `AgentContextMcpTools.UpsertAgentPrompt` | Create/edit a tenant-owned named prompt. Point the step with `agentPrompt`. |
| `list_agent_prompts` | `low` | `none` | No | `AgentContextMcpTools.ListAgentPrompts` | Lists tenant prompt bindings (no API keys). |
| `get_agent_prompt` | `low` | `none` | No | `AgentContextMcpTools.GetAgentPrompt` | Reads one tenant prompt by alias. |
| `explain_validation_violation` | `low` | `none` | No | `AnalysisTools.ExplainValidationViolation` | Explains formal `WorkflowClassValidator` codes (`STR-*`, `CON-*`, `WF-COMP-*`, `GOV-001`, `WF-SLA-*`). |
| `lint_draft_workflowclass` | `low` | `none` | No | `AnalysisTools.LintDraftWorkflowClass` | Advisory linting of private drafts: unreachable states, excessive state count, short step IDs. |
| `create_draft_workflowclass` | `low` | `reversible` | No | `GovernanceTools.CreateDraft` | Authoritatively validates and creates a new private draft blueprint. |
| `update_draft_workflowclass` | `low` | `reversible` | No | `GovernanceTools.UpdateDraft` | Authoritatively validates and updates an existing private draft blueprint. |
| `validate_draft_workflowclass` | `low` | `none` | No | `GovernanceTools.ValidateDraft` | Runs authoritative validation without modifying anything. |
| `get_draft_workflowclass` | `low` | `none` | No | `GovernanceTools.GetDraft` | Reads back full metadata and JSON blueprint of a tenant-owned draft. |
| `list_draft_workflowclasses` | `low` | `none` | No | `GovernanceTools.ListDrafts` | Lists all private draft workflow classes owned by the authenticated tenant. |
| `get_workflow_instance_status` | `low` | `none` | No | `InfoTools.GetWorkflowInstanceStatus` | Queries runtime instance execution status, current step, state machine status, and timestamps. |
| `fork_public_workflowclass` | `low` | `reversible` | No | `GovernanceTools.ForkPublic` | Clones a public template into the caller's private tenant drafts. |
| `publish_workflowclass` | `high` | `irreversible` | **YES** | `GovernanceTools.Publish` | **Publishes draft to immutable versioned fleet status.** Mandates explicit `confirmHumanApproval: true`. |
| `start_workflow` | `medium` | `irreversible` | No | `ExecutionTools.StartWorkflow` | Instantiates and executes a workflow instance. Requires tenant capability `workflow.start`. Missing authority returns `MCP-AUTHZ-001`. Returns the pinned `workflowDefinitionId`/`workflowVersion` and the `businessReference` as `externalSourceSystem`/`externalReference`. |
| `publish_event` | `medium` | `irreversible` | No | `ExecutionTools.PublishEvent` | Emits an event to advance an active workflow instance and state machine. Optional `causationId` records what caused it in the calling system. |
| `complete_task` | `medium` | `irreversible` | No | `ExecutionTools.CompleteTask` | Completes an assigned human or service task step. |
| `diagnose_caller_permissions` | `low` | `none` | No | `TenantIamMcpTools.DiagnoseCallerPermissions` | Reports caller roles, scopes, role capabilities, and the effective API-key intersection. |
| `list_tenant_roles` | `low` | `none` | No | `TenantIamMcpTools.ListTenantRoles` | Lists tenant IAM roles and capabilities. Requires `iam.read`. This is not a WorkflowClass business-role catalog. |
| `create_tenant_role` | `medium` | `reversible` | **YES** | `TenantIamMcpTools.CreateTenantRole` | Creates a tenant IAM role. Requires `iam.manage` and `confirmHumanApproval: true`. |
| `grant_role_capability` | `medium` | `reversible` | **YES** | `TenantIamMcpTools.GrantRoleCapability` | Grants one runtime capability such as `workflow.start`. Requires `iam.manage` and `confirmHumanApproval: true`. |
| `revoke_role_capability` | `medium` | `reversible` | **YES** | `TenantIamMcpTools.RevokeRoleCapability` | Revokes one runtime capability from a tenant IAM role. Requires `iam.manage` and `confirmHumanApproval: true`. |
| `list_workflow_instances` | `low` | `none` | No | `ExecutionTools.ListWorkflowInstances` | Lists workflow instances, filtered by `status`, `parentWorkflowInstanceId`, `sourceSystem` and `externalReference`. Instance fields are PascalCase. |
| `read_change_feed` | `low` | `none` | No | `ChangeFeedMcpTools.ReadChangeFeed` | Reads the tenant's events after a cursor, in storage order: the same feed and v1 shape as `GET /api/changes`, without the HTTP signature. |
| `get_workflow_history` | `low` | `none` | No | `ExecutionTools.GetWorkflowHistory` | Retrieves immutable audit trail and state machine transition history for a tenant's workflow instance. |
| `generate_workflow_blueprint_from_nl` | `low` | `none` | No | `GovernanceTools.GenerateBlueprintFromNaturalLanguage` | Synthesizes or refines a valid `WorkflowClassBlueprint` from a natural-language prompt. |
| `refine_workflow_blueprint_from_nl` | `low` | `none` | No | `GovernanceTools.RefineBlueprintFromNaturalLanguage` | Refines an existing `WorkflowClassBlueprint` incrementally using natural-language instructions and authoritative validation. |
| `replay_workflow_history` | `low` | `none` | No | `ExecutionTools.ReplayWorkflowHistory` | Reconstructs a read-only time-travel timeline of snapshots, tokens, state, variables, and correlated actions. |
| `fork_workflow_simulation` | `low` | `none` | No | `ExecutionTools.ForkWorkflowSimulation` | Sandboxed what-if branch from a historical snapshot; never writes the live instance or Outbox. |
| `simulate_workflowclass` | `low` | `none` | No | `SimulationTools.SimulateWorkflowClass` | Dry-runs a draft or published blueprint without mutating production instances. `roleMap` authorizes each human step or event as a different business role; a mapped role that lacks the capability still stops the run. |
| `simulate_context_binding` | `low` | `none` | No | `ContextBindingMcpTools.Simulate` | Validates and dry-runs a saved draft or exact active context-binding revision with canonical mappings, aliases, roles, and side effects suppressed. Status is `Completed` when the run finishes on a terminal step with no pending work, even if that step is named `End` rather than the sentinel `END`. |
| `simulate_subworkflow` | `low` | `none` | No | `SimulationTools.SimulateSubWorkflow` | Simulates end-to-end parent-child subworkflow execution with parameter input mapping, child step progression, output mapping, and parent resumption. |
| `simulate_parallel_execution` | `low` | `none` | No | `SimulationTools.SimulateParallelExecution` | Dry-runs parallel Fork/Join gateway branches concurrently to evaluate branch actions and barrier synchronization policies. |
| `get_subworkflow_tree` | `low` | `none` | No | `ExecutionTools.GetSubWorkflowTree` | Reconstructs the complete parent-child subworkflow execution hierarchy tree for an instance. |
| `get_instance_action_history` | `low` | `none` | No | `ActionObservabilityMcpTools.GetInstanceActionHistory` | Returns lifecycle action execution logs (webhooks, notifications, events) for an instance. |
| `test_action_plugin` | `low` | `none` | No | `PluginDiscoveryMcpTools.TestActionPlugin` | Dry-runs and validates an action plugin (Email, Slack, WhatsApp, Webhook, etc.) with structured payloads and diagnostics. |
| `get_agent_task_job` | `low` | `none` | No | `AgentTools.GetAgentTaskJob` | Reads the status of a tenant-owned durable agent task job. Use after `run_agent_task` or `suggest_agent_action` returns `pending:true`. |
| `get_agent_execution_history` | `low` | `none` | No | `AgentTools.GetAgentExecutionHistory` | Reads tenant-scoped durable agent execution history with outcome evaluations and token counts. Never returns API keys or prompt bodies. |
| `get_agent_evaluation_metrics` | `low` | `none` | No | `AgentTools.GetAgentEvaluationMetrics` | Calculates governed tenant agent metrics (runs, commits, parks, calibration bins, latency, tokens, provider breakdown) for a UTC window of at most 90 days. |
| `append_agent_chat_message` | `medium` | `reversible` | No | `AgentChatMcpTools.AppendAgentChatMessage` | Appends a user chat message to a workflow instance step conversation and optionally enqueues the step for agent processing (multi-turn MCP). |
| `get_agent_chat_history` | `low` | `none` | No | `AgentChatMcpTools.GetAgentChatHistory` | Reads the ordered multi-turn chat history for one workflow instance step conversation. |
| `list_agent_chat_sessions` | `low` | `none` | No | `AgentChatMcpTools.ListAgentChatSessions` | Lists recent workflow-step chat sessions for the tenant ordered by most recent message. |
| `list_agent_prompt_audits` | `medium` | `none` | No | `AgentContextMcpTools.ListAgentPromptAudits` | Lists tenant prompt/response audit records; pass `includePayloads=true` for raw request/response for forensic debugging. |
| `get_external_agent_settings` | `low` | `none` | No | `ExternalAgentMcpTools.GetExternalAgentSettings` | Reads tenant-level External AI Agent pipeline settings (enabled, autoPilot, profile, autoCommit). |
| `configure_agent_auto_commit` | `high` | `reversible` | No | `ExternalAgentMcpTools.ConfigureAgentAutoCommit` | [Governance] Turns agent auto-commit on or off for the tenant. Turning it on requires `confirmHumanApproval: true`. Admin only. |
| `configure_external_agent_settings` | `medium` | `reversible` | No | `ExternalAgentMcpTools.ConfigureExternalAgentSettings` | Enables or disables the tenant-level External AI Agent pipeline and configures autopilot plus the default planning profile alias. |
| `upsert_agent_provider` | `medium` | `reversible` | No | `AgentContextMcpTools.UpsertAgentProvider` | Creates or edits a tenant-owned LLM provider. `apiKey` is write-only. Supports default-provider selection, fallback alias, maxTokens, and prompt audit logging. |
| `list_agent_providers` | `low` | `none` | No | `AgentContextMcpTools.ListAgentProviders` | Lists tenant-owned LLM providers with model/endpoint/hasApiKey plus default/fallback/audit settings. Never returns the API key. |
| `get_agent_provider` | `low` | `none` | No | `AgentContextMcpTools.GetAgentProvider` | Reads one tenant-owned LLM provider by alias. Returns `hasApiKey`, never the secret. |
| `upsert_agent_tool` | `low` | `reversible` | No | `AgentContextMcpTools.UpsertAgentTool` | Creates or edits a structured tool in the tenant Tool Registry mapping a business alias to an underlying plugin/connector. Security: mutating tools cannot be prefetched. |
| `list_agent_tools` | `low` | `none` | No | `AgentContextMcpTools.ListAgentTools` | Lists registered business tools in the tenant Tool Registry including parameter schemas and security classifications. |
| `get_agent_tool` | `low` | `none` | No | `AgentContextMcpTools.GetAgentTool` | Reads one registered tool by alias from the tenant Tool Registry. |
| `upsert_agent_profile` | `low` | `reversible` | No | `AgentContextMcpTools.UpsertAgentProfile` | Creates or edits a composite AI Agent persona bundling role, provider alias, prompt alias, tool aliases, and autoCommitThreshold. |
| `list_agent_profiles` | `low` | `none` | No | `AgentContextMcpTools.ListAgentProfiles` | Lists composite AI Agent personas (bindingType profile) in tenant AI Context. |
| `get_agent_profile` | `low` | `none` | No | `AgentContextMcpTools.GetAgentProfile` | Reads one composite AI Agent persona by alias or role. |
| `create_context_binding` | `low` | `reversible` | No | `ContextBindingMcpTools.CreateContextBinding` | Creates a tenant-scoped draft context binding and revision 1 from a Draft, Published, or Public workflow template. |
| `update_context_binding` | `low` | `reversible` | No | `ContextBindingMcpTools.UpdateContextBinding` | Updates a draft binding revision; updating an active binding creates its next draft revision without affecting running instances. |
| `validate_context_binding` | `low` | `none` | No | `ContextBindingMcpTools.ValidateContextBinding` | Validates source visibility, aliases, mappings, schemas, roles, capabilities, and decision providers without mutating the binding. |
| `activate_context_binding` | `high` | `irreversible` | **YES** | `ContextBindingMcpTools.ActivateContextBinding` | Compiles and atomically activates the draft as immutable workflow/state-machine/event definitions. Requires `confirmHumanApproval: true`. |
| `archive_context_binding` | `high` | `irreversible` | **YES** | `ContextBindingMcpTools.ArchiveContextBinding` | Archives a context binding and blocks new starts while existing pinned instances continue. Requires `confirmHumanApproval: true`. |
| `list_context_bindings` | `low` | `none` | No | `ContextBindingMcpTools.ListContextBindings` | Lists tenant-scoped context bindings with active and draft revisions. |
| `get_context_binding` | `low` | `none` | No | `ContextBindingMcpTools.GetContextBinding` | Gets one tenant-scoped context binding and its active/draft revision details. |
| `inspect_context_schema` | `low` | `none` | No | `ContextBindingMcpTools.InspectContextSchema` | Inspects the declarative ContextSchema of a WorkflowClass blueprint or active context binding (entityType, primary key, correlationFields, typed fields, computed formulas). |
| `inspect_instance_context` | `low` | `none` | No | `ContextBindingMcpTools.InspectInstanceContext` | Queries the live partitioned Business Context snapshot of a running or completed workflow instance (identity, business data, audit trail, resolved roles). |
| `test_context_operations` | `low` | `none` | No | `ContextBindingMcpTools.TestContextOperations` | Dry-runs Redis-like atomic context primitives (Set, Increment, Append, DeepMerge, SetIfAbsent) against a test snapshot without persisting. |
| `set_instance_priority` | `low` | `reversible` | No | `ExecutionTools.SetInstancePriority` | Sets or clears (null) the task priority of a running workflow instance (0–100, higher is more urgent). The inbox is ordered by priority, then SLA due time, then age. |
| `assign_instance_role` | `medium` | `irreversible` | No | `ExecutionTools.AssignInstanceRole` | Names the one person who holds a declared Assignment business role on a live workflow instance. Only Assignment roles; Expression, Static, and Team roles are rejected. |
| `list_tenant_teams` | `low` | `none` | No | `TenantIamMcpTools.ListTenantTeams` | Lists all teams with required capabilities and hierarchy levels. Requires `iam.read`. |
| `create_tenant_team` | `high` | `reversible` | **YES** | `TenantIamMcpTools.CreateTenantTeam` | Creates a new team with specified capabilities and hierarchy levels. Requires `confirmHumanApproval: true`. |
| `add_team_member` | `high` | `reversible` | **YES** | `TenantIamMcpTools.AddTeamMember` | Adds a user to a team at a specific hierarchy level. Requires `confirmHumanApproval: true`. |
| `register_connector` | `medium` | `reversible` | No | `ConnectorRegistryMcpTools.RegisterConnector` | Creates or updates a tenant-scoped connector mapping an InvokeConnector action name to a remote endpoint contract. Supports transport, timeout, retry profile, and auth reference. |
| `list_connectors` | `low` | `none` | No | `ConnectorRegistryMcpTools.ListConnectors` | Lists tenant-scoped connectors available for InvokeConnector actions. |
| `validate_connector` | `low` | `none` | No | `ConnectorRegistryMcpTools.ValidateConnector` | Validates whether a connector exists, is enabled, uses a supported transport, and has a valid endpoint URL without mutating state. |
| `register_capability_binding` | `medium` | `reversible` | No | `ConnectorRegistryMcpTools.RegisterCapabilityBinding` | **[Deprecated alias]** Use `register_connector`. Identical behaviour; `capabilityName` is accepted as an alias for `connectorName`. |
| `list_capability_bindings` | `low` | `none` | No | `ConnectorRegistryMcpTools.ListCapabilityBindings` | **[Deprecated alias]** Use `list_connectors`. Identical behaviour; response repeats the list under both `connectors` and `bindings`. |
| `validate_capability_binding` | `low` | `none` | No | `ConnectorRegistryMcpTools.ValidateCapabilityBinding` | **[Deprecated alias]** Use `validate_connector`. Identical behaviour; `capabilityName` is accepted as an alias for `connectorName`. |
| `register_plugin_binding` | `medium` | `reversible` | No | `PluginBindingMcpTools.RegisterPluginBinding` | Creates or updates a tenant-scoped mapping from blueprint aliases to server-owned plugins, a tenant-owned LLM, or a named prompt. Agent bindings take apiKey (write-only). |
| `list_plugin_bindings` | `low` | `none` | No | `PluginBindingMcpTools.ListPluginBindings` | Lists tenant plugin bindings (action, decision, agent, or prompt). Agent rows include redacted configuration. |
| `resolve_plugin_binding` | `low` | `none` | No | `PluginBindingMcpTools.ResolvePluginBinding` | Resolves the effective provider for one tenant-scoped source key and reports whether the mapped provider is currently registered on the server. |
| `list_registered_plugins` | `low` | `none` | No | `PluginDiscoveryMcpTools.ListRegisteredPlugins` | Lists action and decision plugins currently registered on the server runtime so AI agents can design with real provider names instead of guessing. |
| `list_dead_letters` | `low` | `none` | No | `ResilienceMcpTools.ListDeadLetters` | Lists dead-lettered outbox messages that exhausted all retry attempts, with failure errors, targets, and attempt counts for AI agent diagnosis. |
| `retry_dead_letter` | `low` | `reversible` | No | `ResilienceMcpTools.RetryDeadLetter` | Resets retry counts and immediately re-enqueues one or all dead letters back into the active Outbox when a downstream API has recovered. |
| `purge_dead_letter` | `medium` | `irreversible` | No | `ResilienceMcpTools.PurgeDeadLetter` | Permanently deletes an unrecoverable dead letter from the transactional outbox to maintain queue hygiene. |
| `verify_webhook_signature` | `low` | `none` | No | `WebhookSecurityMcpTools.VerifyWebhookSignature` | Validates or generates HMAC-SHA256 signatures (X-FlowOS-Signature: t={ts},v1={hash}) for a given payload string and signing secret. |
| `test_webhook_endpoint` | `medium` | `reversible` | No | `WebhookSecurityMcpTools.TestWebhookEndpoint` | Dispatches a live pre-flight probe ping to an external HTTP endpoint with full cryptographic headers; measures latency and reports HTTP status codes. |
| `rotate_webhook_secret` | `high` | `irreversible` | No | `WebhookSecurityMcpTools.RotateWebhookSecret` | Zero-downtime key rotation: generates a fresh 32-byte HMAC-SHA256 signing secret. Existing webhooks immediately sign using the new key. |
| `attach_step_action` | `low` | `reversible` | No | `GovernanceTools.AttachStepAction` | Attaches or updates a declarative lifecycle action (Webhook, Notification, PublishEvent, InvokeCapability, or plugin-prefixed alias) on a step's OnEntry, OnExit, or OnFailure hook in a draft. |
| `remove_step_action` | `low` | `reversible` | No | `GovernanceTools.RemoveStepAction` | Removes a declarative lifecycle action from a step's OnEntry, OnExit, or OnFailure hook in a draft WorkflowClass by index, actionType, or target. |
| `list_step_actions` | `low` | `none` | No | `GovernanceTools.ListStepActions` | Lists configured OnEntry, OnExit, and OnFailure lifecycle actions for a specific step (or all steps) in a draft or published WorkflowClass. |
| `simulate_compensation_path` | `low` | `none` | No | `SimulationTools.SimulateCompensationPath` | [Saga Compensation Planner] Produces a deterministic compensation rollback path from a failed step by evaluating configured OnFailure hooks in reverse (LIFO). Analysis-only. |
| `plan_workflow_compensation_path` | `low` | `none` | No | `ResilienceMcpTools.PlanWorkflowCompensationPath` | [Saga Analysis] Computes an execution-aware compensation rollback path for a live instance using immutable replay history and configured OnFailure hooks. Analysis-only. |
| `register_idempotency_key` | `low` | `reversible` | No | `ReliabilityMcpTools.RegisterIdempotencyKey` | Reserves a tenant-scoped idempotency key for a named operation before executing mutating calls; returns `registered:false` if the key already exists. |
| `inspect_idempotency_status` | `low` | `none` | No | `ReliabilityMcpTools.InspectIdempotencyStatus` | Retrieves the status of a tenant-scoped idempotency key (Pending → Completed / Failed). |
| `preview_retry_policy` | `low` | `none` | No | `ReliabilityMcpTools.PreviewRetryPolicy` | Previews retry scheduling behavior (exponential, linear, constant) and classifies error strings as transient or permanent to help callers tune retry policy. |
| `poll_external_agent_changes` | `medium` | `reversible` | No | `ExternalAgentMcpTools.PollExternalAgentChanges` | Polls the next batch of tenant-scoped change records for an external autonomous AI agent; atomically leases each record unless `peekOnly=true`. |
| `ack_external_agent_change` | `medium` | `reversible` | No | `ExternalAgentMcpTools.AckExternalAgentChange` | Idempotently acknowledges a leased external agent change record as succeeded or failed (increments attempt, may promote to DeadLetter). |
| `renew_change_lease` | `medium` | `reversible` | No | `ExternalAgentMcpTools.RenewChangeLease` | Extends the lease TTL on a Leased change record so a long-running external agent avoids having the record reclaimed mid-processing. |
| `list_external_agent_changes` | `low` | `none` | No | `ExternalAgentMcpTools.ListExternalAgentChanges` | Read-only observability scan of the tenant's pending/failed change queue. Use `poll_external_agent_changes` to acquire leases for actual processing. |
| `get_external_agent_change` | `low` | `none` | No | `ExternalAgentMcpTools.GetExternalAgentChange` | Retrieves a single change record by UUID joined with any associated ExternalAgentPlan steps derived from the originating outbox message. |
| `plan_for_change` | `medium` | `reversible` | No | `ExternalAgentMcpTools.PlanForChange` | Uses the tenant's configured LLM provider to transform one external-agent change into a structured MCP execution plan. Unknown tool names are dropped during validation. |
| `execute_plan` | `high` | `irreversible` | No | `ExternalAgentMcpTools.ExecutePlan` | Runs each persisted plan step sequentially against the MCP tool registry; a failed step aborts dependent work and marks downstream steps Skipped. |
| `resume_plan` | `high` | `reversible` | No | `ExternalAgentMcpTools.ResumePlan` | Resets failed/skipped steps back to Pending from the selected step boundary and re-runs the remaining plan sequentially. |
| `get_external_agent_plan` | `low` | `none` | No | `ExternalAgentMcpTools.GetExternalAgentPlan` | Retrieves a persisted external-agent plan and all step execution statuses for observability and debugging. |

For HTTP, the authenticated `x-tenant-id` header is authoritative. A `tenantId`
tool argument may repeat that value but cannot override it. For stdio, every
tenant-scoped tool requires an explicit `tenantId` argument.

### Integration contract over MCP (Learning OS)

The MCP tools expose the same integration contract as the HTTP API ([Chapter 14](14-api-reference.md)):

* **References.** Start with `businessReference: {sourceSystem, externalEntityId}`. `start_workflow` returns `workflowDefinitionId`, `workflowVersion`, `externalSourceSystem` and `externalReference`. `list_workflow_instances` finds the instance again by `sourceSystem` and `externalReference`.
* **Causation.** `publish_event` accepts `causationId` (a UUID from the calling system). It is stored in the event metadata and returned by the change feed.
* **Safe retries.** Reuse the same `idempotencyKey` when retrying `start_workflow`, `publish_event` or `complete_task`.
* **Conflicts.** `MCP-CONFLICT-001` with context `{conflictCode, retryable: true}` means another update won the race (`CONCURRENT-UPDATE`) or a request with the same key is still running (`IDEMPOTENCY-IN-PROGRESS`). Read the instance again, then retry. It is the MCP equivalent of HTTP `409`.
* **Change feed.** `read_change_feed` takes `after` (the previous `nextCursor`) and `limit` (1–500, default 50). It is read-only and meant for agents checking what happened. Integrating systems should poll the signed `GET /api/changes`.

### Enforced Governance & Safety Policy

1. **Human Confirmation Gate (`confirmHumanApproval`)**:
   `publish_workflowclass` and tenant IAM mutations (`create_tenant_role`, `grant_role_capability`, `revoke_role_capability`) mandate `confirmHumanApproval: true`. Invocations without this parameter are blocked before executing domain logic:
   ```json
   {
     "ok": false,
     "errorCode": "MCP-APPROVAL-REQUIRED",
     "message": "MCP-APPROVAL-REQUIRED: Operation 'publish_workflowclass' has high risk impact (irreversible fleet/public publication). Explicit human confirmation ('confirmHumanApproval': true) is required."
   }
   ```

2. **Cross-Tenant Public Blueprints**:
   `start_workflow` allows launching workflows defined with `Scope == Public` across tenant boundaries. The definition blueprint is retrieved from the publisher, but the runtime instance, state machine, events, and audit logs are strictly owned by the caller's tenant.

3. **Anti-Enumeration & Uniformity (`MCP-NOTFOUND-001`)**:
   Attempts to access foreign tenant resources (drafts, instances, histories) return the exact same `MCP-NOTFOUND-001` error as non-existent random GUIDs. Zero metadata (existence, title, or status) is leaked.

4. **Authorization (`MCP-AUTHZ-001`)**:
   `start_workflow` requires tenant capability `workflow.start`. API-key scopes such as `workflow:start` are an outer boundary, not a grant. Diagnose with `diagnose_caller_permissions`, then grant the capability on the reserved `Admin` or `ApiKey` role.

### Compensation governance MCP guideline (recommended authoring loop)

1. Run `lint_draft_workflowclass` first and inspect warnings for `LINT-COMP-001` (side-effect hooks without compensation).
2. Add `OnFailure` hooks using `attach_step_action` for each flagged step.
3. Run `validate_draft_workflowclass` and treat `WF-COMP-010` as a blocking safety error.
4. Use `simulate_compensation_path` on draft blueprints to validate design-time rollback order (LIFO).
5. Use `plan_workflow_compensation_path` on live instances to verify execution-aware compensation paths from replay history.
6. If violations persist, call `explain_validation_violation` with the code and step context to generate a remediation hint for the agent/human.

### Connector MCP guideline (hybrid extension path)

A **connector** is an outbound tenant worker reached over HTTP. It is not a security capability — that is a role
permission code ([Chapter 8](08-security-roles-and-policies.md)).

1. Register or update the connector with `register_connector` (connector name + endpoint URL + timeout/retry profile).
2. Run `validate_connector` and ensure `isValid=true` before wiring steps.
3. Attach an `InvokeConnector` action using `attach_step_action` and set `action.connector` explicitly.
4. Keep `OnFailure` compensation hooks on side-effecting steps to satisfy `WF-COMP-010`.
5. Use `list_connectors` to audit enabled/disabled connectors during rollouts.

Deprecated but still accepted: the tool names `register_capability_binding`, `list_capability_bindings`, and
`validate_capability_binding`; the argument `capabilityName`; the action type `InvokeCapability`; and the action
field `capability`. FlowOS normalizes all of them onto the connector shape, so existing blueprints and saved agent
scripts keep working without edits.

### Plugin discovery & action plugin capabilities (AI design loop)

1. Call `list_registered_plugins` to discover server-registered action and decision providers, runtime strictness flags, and rich capability metadata (category, description, supported parameters with types/required flags, and blueprint-ready example payload mappings) for communication (`Email`, `Slack`, `WhatsApp`), integration (`Webhook`), and internal action plugins.
2. Use `test_action_plugin` to test/dry-run action configurations and verify channel deliverability parameters (such as email recipient syntax, WhatsApp E.164 phone numbers, Slack blocks, and webhook URLs) without mutating state or enqueuing outbox records.
3. Use `register_plugin_binding` to map tenant aliases (`plugin:*` / `plugin.*` action types or decision provider aliases) to concrete providers, **or** `bindingType: agent` for a tenant-owned LLM (`configuration` `{model,endpoint,apiKey}`; the key is write-only).
4. Verify each alias with `resolve_plugin_binding` before publishing. Agent bindings report `hasApiKey`, never the secret.
5. Use `list_plugin_bindings` to audit all active mappings for the tenant.
6. On an agent-handled waiting step, set `agentProvider` to the agent-binding alias and `agentTools` to resource plugins plus notify/write aliases. FlowOS puts them on DecisionPacket.Tools; the model does not call HTTP itself.
7. Tenant resources are connectors, not plugin DLLs. Register the URL with `register_connector`, then declare `LookupRecord:<connector>`, `QueryRecords:`, `FetchDocument:`, `SearchKnowledge:`, or `CheckPolicy:` on the waiting step. FlowOS prefetches those reads into Agent Context. Write APIs stay `connector:<name>` (legacy `capability:<name>`) and are not prefetched. The model never sees the endpoint URL.

## Usage example: design loop for "Leave Approval"

### 1. Discovery

```json
{ "jsonrpc": "2.0", "id": 1, "method": "tools/list", "params": {} }
```

### 2. Propose a Draft — and see it rejected

Note the **verified field names**: `eventId` (not `eventType`), and no `label`/`config` on steps.

> **Important:** `create_draft_workflowclass` (`GovernanceTools.CreateDraft`) runs `WorkflowClassValidator` **before** saving and never persists an invalid Draft — it returns `isError: true` immediately instead. `update_draft_workflowclass` does the same. This means an invalid WorkflowClass can never actually reach the database via MCP (or via the REST API's `POST /api/workflow-classes`, which enforces the same rule — see [Chapter 9](09-workflow-class-governance.md#rest-api--verified-against-workflowclassescontroller)). The example below is deliberately missing the top-level `capabilities` array even though the `Manager` role grants `event.publish.EVT-APPROVE` — the single most common authoring mistake — to show you exactly what that rejection looks like.

```json
{
  "jsonrpc": "2.0", "id": 2, "method": "tools/call",
  "params": {
    "name": "create_draft_workflowclass",
    "arguments": {
      "name": "LeaveApproval",
      "version": "1.0.0",
      "blueprint": {
        "events": [
          { "eventId": "EVT-SUBMIT", "category": "Human", "requiredCapabilities": ["event.publish.EVT-SUBMIT"] },
          { "eventId": "EVT-APPROVE", "category": "Human", "requiredCapabilities": ["event.publish.EVT-APPROVE"] }
        ],
        "stateMachine": {
          "initialState": "Draft",
          "states": ["Draft", "Submitted", "Approved"],
          "transitions": [
            { "fromState": "Draft", "eventId": "EVT-SUBMIT", "toState": "Submitted" },
            { "fromState": "Submitted", "eventId": "EVT-APPROVE", "toState": "Approved" }
          ]
        },
        "workflow": {
          "startStepId": "SubmitStep",
          "steps": [
            { "stepId": "SubmitStep", "stepType": "HumanTask", "nextSteps": { "EVT-SUBMIT": "ApprovalStep" }, "requiredRoles": ["Employee"], "requiredCapabilities": ["event.publish.EVT-SUBMIT"] },
            { "stepId": "ApprovalStep", "stepType": "HumanTask", "nextSteps": { "EVT-APPROVE": "END" }, "requiredRoles": ["Manager"], "requiredCapabilities": ["event.publish.EVT-APPROVE"] }
          ]
        },
        "roles": [{ "name": "Manager", "grantedCapabilities": ["event.publish.EVT-APPROVE"] }]
      }
    }
  }
}
```

`GovernanceTools.CreateDraft` only joins each error's `Message` text (not its `Code`) into the response, so the rejection looks like this — **no error code is surfaced at this stage**:

```json
{ "jsonrpc": "2.0", "id": 2, "result": { "content": [{ "type": "text", "text": "Failed to create draft: Validation Failed: Role 'Manager' grants undeclared capability 'event.publish.EVT-APPROVE'" }], "isError": true } }
```

### 3. Fix it, retry, then confirm with `validate_draft_workflowclass`

Add the missing `capabilities` array to the same `arguments.blueprint` (`"capabilities": [{ "code": "event.publish.EVT-SUBMIT" }, { "code": "event.publish.EVT-APPROVE" }]`) and grant `event.publish.EVT-SUBMIT` on Employee. Resend the same `create_draft_workflowclass` call. This time it saves:

```json
{ "jsonrpc": "2.0", "id": 2, "result": { "content": [{ "type": "json", "text": "{ \"id\": \"<GUID>\", \"status\": \"Draft\", \"message\": \"Draft created successfully\" }" }], "isError": false } }
```

Since only validated Drafts can ever be persisted, calling `validate_draft_workflowclass` on this (or any) existing id will always come back clean:

```json
{ "jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": { "name": "validate_draft_workflowclass", "arguments": { "id": "<GUID>" } } }
```

```json
{ "isValid": true, "errors": [] }
```

### 4. Explain a real validator code

```json
{ "jsonrpc": "2.0", "id": 4, "method": "tools/call", "params": { "name": "explain_validation_violation", "arguments": { "code": "GOV-001", "context": {} } } }
```

```json
{ "code": "GOV-001", "humanExplanation": "A role grants a capability that is not declared in Capabilities.", "designHint": "Declare capability '…' under Capabilities, or remove it from the role." }
```

MCP governance tools (`create`/`update`/`validate`/`fork`/`list_public`) now go through Application MediatR + `IUnitOfWork` (same path as the REST API), with an MCP `ICurrentUser` ambient tenant.

### Reuse a published template through context bindings

The seven binding governance tools are `create_context_binding`, `update_context_binding`, `validate_context_binding`, `activate_context_binding`, `archive_context_binding`, `list_context_bindings`, and `get_context_binding`. `simulate_context_binding` is a separate read-only analysis tool. A draft binding may point at a Draft workflow class so agents can simulate a business payload without publishing a throwaway variant. `validate_context_binding` and `activate_context_binding` still require a Published or Public source. WorkflowClass roles are business-context declarations compiled onto `WorkflowDefinition.BusinessRoles`; activation never writes FlowOS tenant Role rows. `CTX-ROLE-002` only means a role override mapped to an empty name. After activation, call the existing `start_workflow` with `contextBindingId` or `contextType`. Activation and archival require `confirmHumanApproval: true`. Full mapping and simulation examples are in [Chapter 17](17-workflow-context-bindings.md).

Agents should load MCP prompt `design_dual_kernel_workflow` or resource `flowos://guides/dual-kernel-design` before authoring Decision steps. Workflow `currentStep` and state-machine `currentState` move independently. A Decision `Default`/`true` auto-route does not consume a business event; include that event in `simulate_workflowclass` / `simulate_context_binding` so FlowOS can apply it as state-only catch-up. To prove SLA reminders vs timeout, load prompt `test_sla_reminders_in_simulator` or resource `flowos://guides/sla-reminder-simulation`: a simulate-to-Paid run is not a wall clock (completing event means in-time; look for `[SLA Reminder Fired]`); overdue requires `autoAdvanceTimers: true` with the completing event omitted. Do not start a live instance to wait. For agent-handled waiting steps, load `design_agent_handled_step` / `flowos://guides/bounded-autonomy-tasks`: keep a HumanTask/Command wait, set `actor` Agent/Either, put how-to-decide on `decisionGuideline` and tenant policy on binding `policyGuideline`, point `agentProvider` at a tenant `agent` plugin binding (BYO model/key, never in Agent Context), declare `agentTools` as resource plugins (`LookupRecord:<capability>`, `QueryRecords:`, `FetchDocument:`, `SearchKnowledge:`, `CheckPolicy:`) plus notify/write capabilities, and auto-commit only events listed on both `nextSteps` and `autoCommit.allowedEvents`. Context bindings never create tenant roles. Do not publish a stripped-roles simulator class just to bind; bind the draft and simulate, then publish once.

### 5. Fork a public template instead of starting from scratch

```json
{ "jsonrpc": "2.0", "id": 5, "method": "tools/call", "params": { "name": "fork_public_workflowclass", "arguments": { "publicId": "<Public_GUID>", "tenantId": "<My_Tenant_ID>" } } }
```

## Declarative AI Agent Workflow design (ADR-006 Phase 4A)

An `AgentWorkflowClass` is a **declarative multi-agent coordination plan template** — the agentic counterpart of `WorkflowClass`. It stores an immutable `AgentWorkflowBlueprint` (the "what") and will produce `AgentWorkflowExecution` runtime instances (the "how", Phase 4B). Use it when multiple AI agent steps must be sequenced declaratively, versioned, audited, and governed.

> **Phase boundary**: Phase 4A (current) is blueprint CRUD only — create, read, update, publish, deprecate, abandon, delete. Phase 4B will add the dispatch engine and `start_agent_workflow` MCP tooling.

### How it differs from existing constructs

| | `WorkflowClass` | `AgentWorkflowClass` |
|---|---|---|
| Actor | Humans, events, agents (mixed) | AI agents only |
| State machine | Yes | No |
| Event registry | Yes | No |
| Blueprint type | `WorkflowClassBlueprint` (stepTypes, roles) | `AgentWorkflowBlueprint` (steps, routing, fork/join, SLA) |
| Dispatch | `start_workflow` → `WorkflowInstance` | `start_agent_workflow` (Phase 4B) → `AgentWorkflowExecution` |
| Versioning | `PreviousVersionId` lineage chain | Same pattern |

### Blueprint shape

```json
{
  "objective": "Describe what the plan achieves",
  "startStepId": "gather",
  "coordinationRules": {
    "maxGlobalRetries": 3,
    "rollbackStrategy": "Compensate",
    "globalSla": "PT4H",
    "maxParallelSteps": 2
  },
  "steps": [
    {
      "stepId": "gather",
      "description": "Gather risk data",
      "agentTeamId": "<team-guid>",
      "nextSteps": { "Done": "review" },
      "autoCommit": { "minConfidence": 0.90 },
      "sla": { "duration": "PT2H", "escalationRole": "RiskManager" }
    },
    {
      "stepId": "review",
      "description": "Human-in-the-loop review gate",
      "consequential": true,
      "nextSteps": { "Approved": null, "Rejected": "gather" },
      "pathLimits": { "Rejected": { "maxTravels": 2 } }
    }
  ]
}
```

### Lifecycle

```
Draft → Published → Deprecated
                  ↘ Abandoned
```

* **Draft** — editable, not dispatchable. Default on creation.
* **Published** — immutable, dispatchable. Create a new version to change it.
* **Deprecated** — no new executions; existing executions continue to completion. Set `migrationTargetId` to point agents to the replacement version.
* **Abandoned** — soft-deleted; informational only.

Governance guards enforced by the domain model:
* **GOV-DEL-001** — Only `Draft` plans may be hard-deleted. Use `Abandon` for published plans.
* **GOV-DEL-002** — A `Draft` with existing executions cannot be deleted.

### MCP guidance

Load the MCP prompt or resource before authoring a plan:

```json
{ "jsonrpc": "2.0", "id": 1, "method": "prompts/get",
  "params": { "name": "design_agent_workflow_class",
               "arguments": { "objective": "QuarterlyRiskAssessment" } } }
```

Or read the resource directly:

```json
{ "jsonrpc": "2.0", "id": 2, "method": "resources/read",
  "params": { "uri": "flowos://guides/agent-workflow-class" } }
```

> **Note**: MCP *execution* tools (`create_agent_workflow_class`, `start_agent_workflow`) are not yet registered — they ship in Phase 4B. The guidance prompt and resource are available now for design-time authoring assistance.

## Gap analysis — 10/10 Verification Status

Previously identified MCP control-plane gaps are covered by the maintained `FlowOS.MCP.UnitTests` contract suite:

* **Read Gap**: Fully resolved via `get_draft_workflowclass`, `list_draft_workflowclasses`, `list_public_workflowclasses`, `describe_workflowclass_schema`, `get_workflow_instance_status`, `list_workflow_instances`, and `get_workflow_history`.
* **Execution Boundary**: Fully implemented with `start_workflow`, `publish_event`, and `complete_task`, enforcing strict tenant boundaries and supporting cross-tenant public workflows.
* **Human Approval Enforcement**: High-risk, irreversible operations (`publish_workflowclass`, `activate_context_binding`, and `archive_context_binding`) require `confirmHumanApproval: true`, returning `MCP-APPROVAL-REQUIRED` on missing confirmation.
* **Adversarial BOLA/IDOR Protection**: All foreign resource access attempts are denied and normalized to `MCP-NOTFOUND-001`, eliminating information leakage and existence oracles.
* **Dual Discovery & Execution**: Public `GET` discovery (interactive HTML or JSON schema metadata, including `connection.jsonrpcUrl`) while `POST` to that advertised URL enforces authentication, tenant isolation, and risk policies. Production agents must keep the trailing slash on `https://flowosbd.com/mcp/`.

## Where to go next

* [Chapter 9 — WorkflowClass Governance](09-workflow-class-governance.md) for the REST equivalent of the same lifecycle.
* [Chapter 15 — Known Limitations](15-known-limitations-and-gaps.md) for core engine boundaries.
* [Chapter 19 — OS-1 Honesty Gate](19-os-release-gate.md) before calling FlowOS a business automation OS (`check_os_release_gate` / `flowos://guides/os-release-gate`). If `VERDICT` is GREEN, use the OS sentence; otherwise it is a dual-kernel process engine with MCP.
* [Chapter 23 — Tenant backup](23-tenant-backup.md) before moving a DesignedApp and its running instances (`backup_tenant_for_another_site` / `flowos://guides/tenant-backup`). The file is a download, not an MCP tool payload.
