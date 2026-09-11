# 13. MCP & AI Agent Control Plane

FlowOS is a **multi-tenant, state-machine-governed workflow control plane with an MCP interface for safe AI-agent interaction**.

Unlike standard MCP servers that merely expose thin CRUD wrappers, FlowOS functions as an authoritative control plane. Agents can reason, design, lint, execute, and inspect workflows, while FlowOS enforces strict tenant boundaries, formal schema validation, state-machine invariants, side-effect classifications, and mandatory human approval gates for irreversible fleet operations.

## Architecture: Discovery & Execution Separation

```
GET /mcp  ──► Public Discovery (HTML / JSON metadata, schemas, risk levels, human confirmation)
POST /mcp ──► Authenticated JSON-RPC 2.0 (tenant resolution, object-level IDOR check, human gates)
```

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
| `GET` | `/mcp` | **Public Discovery**: Returns interactive HTML documentation (Accept: `text/html`) or machine-readable JSON metadata (Accept: `application/json`) including all 21 tool schemas, risk levels, side effects, and confirmation requirements without requiring credentials. |
| `POST` | `/mcp` | **Protected Execution**: Authenticated JSON-RPC 2.0 body (`initialize`, `tools/list`, `tools/call`). Requires `x-tenant-id` and API key/bearer. |
| `OPTIONS`| `/mcp` | CORS preflight handling for web/browser agent environments. |
| `GET` | `/health` | `200` `{ "status": "ok" }` |

> 💡 **Pre-Payment Gateway & Sandbox Mode**:
> Setting `MCP_API_KEY=disabled` (or leaving `MCP_API_KEY` unconfigured) bypasses the `X-MCP-API-Key` header requirement for easy testing and AI agent evaluation. When set to a secret string, `X-MCP-API-Key` is strictly enforced. Every request still requires a valid `x-tenant-id`.

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

FlowOS registers **21 production tools** categorized by governance lifecycle, operational execution, and runtime advisory intelligence:

| Tool name | Risk Level | Side Effect | Requires Human Confirmation | Implementation | Description |
|---|---|---|---|---|---|
| `describe_workflowclass_schema` | `low` | `none` | No | `InfoTools.DescribeSchema` | Returns JSON schema aligned with `WorkflowClassBlueprint` (`eventId`, roles/capabilities, real `stepTypes`). |
| `list_public_workflowclasses` | `low` | `none` | No | `InfoTools.ListPublic` | Lists `{ id, name, version }` for every `Public`-scope WorkflowClass blueprint. |
| `list_notifications` | `low` | `none` | No | `NotificationTools.ListNotifications` | Lists recent tenant and user notifications with severity levels. |
| `mark_notification_as_read` | `low` | `reversible` | No | `NotificationTools.MarkNotificationAsRead` | Marks a specific notification as read. |
| `list_available_agents` | `low` | `none` | No | `AgentTools.ListAvailableAgents` | Lists registered runtime agents (e.g. `RiskAnalysisAgent`) and their capabilities. |
| `suggest_agent_action` | `low` | `none` | No | `AgentTools.SuggestAgentAction` | Runs advisory reasoning against tenant-isolated instance state to return a `SuggestedAction`. |
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
| `start_workflow` | `medium` | `irreversible` | No | `ExecutionTools.StartWorkflow` | Instantiates and executes a workflow instance. Supports cross-tenant public blueprints with caller data isolation. |
| `publish_event` | `medium` | `irreversible` | No | `ExecutionTools.PublishEvent` | Emits an event to advance an active workflow instance and state machine. |
| `complete_task` | `medium` | `irreversible` | No | `ExecutionTools.CompleteTask` | Completes an assigned human or service task step. |
| `list_workflow_instances` | `low` | `none` | No | `ExecutionTools.ListWorkflowInstances` | Lists workflow instances filtered by status (`Active`, `Completed`, `Failed`) and workflow name. |
| `get_workflow_history` | `low` | `none` | No | `ExecutionTools.GetWorkflowHistory` | Retrieves immutable audit trail and state machine transition history for a tenant's workflow instance. |

For HTTP, the authenticated `x-tenant-id` header is authoritative. A `tenantId`
tool argument may repeat that value but cannot override it. For stdio, every
tenant-scoped tool requires an explicit `tenantId` argument.

### Enforced Governance & Safety Policy

1. **Human Confirmation Gate (`confirmHumanApproval`)**:
   `publish_workflowclass` has a high risk profile and creates immutable fleet-wide artifacts. It mandates `confirmHumanApproval: true`. Invocations without this parameter are blocked before executing domain logic:
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
          { "eventId": "EVT-SUBMIT", "category": "Human" },
          { "eventId": "EVT-APPROVE", "category": "Human" }
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
            { "stepId": "SubmitStep", "stepType": "HumanTask", "nextSteps": { "EVT-SUBMIT": "ApprovalStep" }, "requiredRoles": ["Employee"] },
            { "stepId": "ApprovalStep", "stepType": "HumanTask", "nextSteps": { "EVT-APPROVE": "END" }, "requiredRoles": ["Manager"] }
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

Add the missing `capabilities` array to the same `arguments.blueprint` (`"capabilities": [{ "code": "event.publish.EVT-APPROVE" }]`) and resend the same `create_draft_workflowclass` call. This time it saves:

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

### 5. Fork a public template instead of starting from scratch

```json
{ "jsonrpc": "2.0", "id": 5, "method": "tools/call", "params": { "name": "fork_public_workflowclass", "arguments": { "publicId": "<Public_GUID>", "tenantId": "<My_Tenant_ID>" } } }
```

## Gap analysis — 10/10 Verification Status

All previously identified MCP control plane gaps have been completely resolved and verified against the 25-test suite in `FlowOS.MCP.UnitTests`:

* **Read Gap**: Fully resolved via `get_draft_workflowclass`, `list_draft_workflowclasses`, `list_public_workflowclasses`, `describe_workflowclass_schema`, `get_workflow_instance_status`, `list_workflow_instances`, and `get_workflow_history`.
* **Execution Boundary**: Fully implemented with `start_workflow`, `publish_event`, and `complete_task`, enforcing strict tenant boundaries and supporting cross-tenant public workflows.
* **Human Approval Enforcement**: High-risk, irreversible operations (`publish_workflowclass`) require `confirmHumanApproval: true`, returning `MCP-APPROVAL-REQUIRED` on missing confirmation.
* **Adversarial BOLA/IDOR Protection**: All foreign resource access attempts are denied and normalized to `MCP-NOTFOUND-001`, eliminating information leakage and existence oracles.
* **Dual Discovery & Execution**: `GET /mcp` provides zero-auth public discovery (interactive HTML or JSON schema metadata) while `POST /mcp` enforces authentication, tenant isolation, and risk policies.

## Where to go next

* [Chapter 9 — WorkflowClass Governance](09-workflow-class-governance.md) for the REST equivalent of the same lifecycle.
* [Chapter 15 — Known Limitations](15-known-limitations-and-gaps.md) for core engine boundaries.
