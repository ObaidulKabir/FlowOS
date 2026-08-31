# 13. MCP & AI Agent Automation

FlowOS ships a standalone **Model Context Protocol (MCP)** server (`src/FlowOS.MCP`) that lets an external AI model design and govern `WorkflowClass` blueprints ([Chapter 9](09-workflow-class-governance.md)) over JSON-RPC 2.0. This is a **design-time-only** surface, completely separate from the runtime agent pattern in [Chapter 7](07-ai-agents-and-insights.md).

## Running the MCP server

### Stdio (default — Cursor local MCP)

```bash
dotnet run --project src/FlowOS.MCP/FlowOS.MCP.csproj
```

Communicates over **stdio** (`stdin`/`stdout`) using JSON-RPC 2.0. Logging goes to stderr/Debug so it never pollutes the JSON-RPC stream on stdout.

### Streamable HTTP

```bash
# PowerShell
$env:MCP_TRANSPORT="http"
$env:ASPNETCORE_URLS="http://0.0.0.0:8080"
dotnet run --project src/FlowOS.MCP/FlowOS.MCP.csproj
```

```bash
# bash
MCP_TRANSPORT=http ASPNETCORE_URLS=http://0.0.0.0:8080 \
  dotnet run --project src/FlowOS.MCP/FlowOS.MCP.csproj
```

Endpoints:

| Method | Path | Behavior |
|--------|------|----------|
| `POST` | `/mcp` | JSON-RPC body → `application/json` response (or `202` for notifications) |
| `GET` | `/mcp` | `405` (no standalone SSE listen stream) |
| `GET` | `/health` | `200` `{ "status": "ok" }` |

Optional headers (same mock auth style as the API): `x-tenant-id`, `X-Mock-Role`.

Smoke test:

```bash
curl -s http://localhost:8080/health

curl -s -X POST http://localhost:8080/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}}}'

curl -s -X POST http://localhost:8080/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
```

Cursor remote MCP example (`mcp.json`):

```json
{
  "mcpServers": {
    "flowos": {
      "url": "http://localhost:8080/mcp"
    }
  }
}
```

Docker Compose runs MCP in HTTP mode by default (`MCP_TRANSPORT=http`, port **8081→8080** locally; Traefik `PathPrefix(/mcp)` in `docker-compose.test.yaml`).

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

## MCP tool reference — verified against `FlowOS.MCP.Program.RegisterTools()`

| Tool name | Arguments | Implementation | Description |
|---|---|---|---|
| `describe_workflowclass_schema` | _none_ | `InfoTools.DescribeSchema` | Returns a JSON schema aligned with `WorkflowClassBlueprint` (`EventId`, Roles/Capabilities, real StepTypes). |
| `list_public_workflowclasses` | `tenantId` (opt) | `InfoTools.ListPublic` | Lists `{ id, name, version }` for every `Public`-scope WorkflowClass (via Application MediatR/UoW). |
| `list_available_agents` | _none_ | `AgentTools.ListAvailableAgents` | Lists registered runtime agents (currently hardcoded: `RiskAnalysisAgent`) and their capabilities. |
| `suggest_agent_action` | `workflowInstanceId`, `agentId` | `AgentTools.SuggestAgentAction` | Runs a real `IWorkflowAgent` against a workflow instance's latest event payload (or a simulated payload if none exists) and returns its `SuggestedAction`. |
| `explain_validation_violation` | `code`, `context` (json) | `AnalysisTools.ExplainValidationViolation` | Explains real `WorkflowClassValidator` codes (`STR-*`, `CON-*`, `WF-COMP-*`, `GOV-001`, etc.). |
| `lint_draft_workflowclass` | `id`, `tenantId` (opt) | `AnalysisTools.LintDraftWorkflowClass` | Advisory, non-blocking lint: orphaned events, excessive state count (>15), overly short Step IDs. |
| `create_draft_workflowclass` | `name`, `version`, `blueprint`, `tenantId` | `GovernanceTools.CreateDraft` | Creates a new Draft via `CreateWorkflowClassCommand`. **Fails if authoritative validation fails.** |
| `update_draft_workflowclass` | `id`, `blueprint`, `tenantId`, `name`/`version` (opt) | `GovernanceTools.UpdateDraft` | Updates an existing Draft via `UpdateWorkflowClassCommand`. **Requires `tenantId`.** |
| `validate_draft_workflowclass` | `id`, `tenantId` | `GovernanceTools.ValidateDraft` | Runs authoritative validation without modifying anything. **Requires `tenantId`.** |
| `fork_public_workflowclass` | `publicId`, `tenantId` | `GovernanceTools.ForkPublic` | Creates a private Draft copy of a `Public` template via `CopyWorkflowClassCommand`. |

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

## Gap analysis — updated

An earlier internal review (`MCP_Review_And_Gap_Analysis.md`, now consolidated here) identified a "Read Gap": agents had no way to see the existing world before proposing changes. Re-checked against the current code:

**Closed since that review:**
* `list_available_agents`, `suggest_agent_action` — runtime agent discovery/simulation now exist.
* `list_public_workflowclasses` — public template discovery now exists.
* `describe_workflowclass_schema` — schema now matches `WorkflowClassBlueprint` (`EventId`, Roles/Capabilities, real StepTypes).
* `explain_validation_violation` — knowledge base aligned with real validator codes.
* MCP write/list tools routed through Application MediatR + UoW (no direct DbContext in tool classes).

**Still open (recommended, not yet implemented):**
* `get_workflowclass(id)` — read a single existing (including your own private) WorkflowClass by id via MCP before modifying it. Today an agent can only create new Drafts, list Public ones, or fork; it cannot fetch its own tenant's existing Draft content back out through MCP.
* `list_workflowclasses(tenantId)` filtered by scope/status other than `Public` — the REST API's `GET /api/workflow-classes` supports this; MCP doesn't expose an equivalent.
* Diagnostic tools (`get_workflow_instance_trace`, `search_event_log`) for read-only runtime observability, per the constitution's §11.15 allowance — not implemented.
* Documentation-as-resource (`flowos://docs/invariants`, `flowos://docs/api`) — not implemented; this guide is currently only accessible as files in the repo.

## Where to go next

* [Chapter 9 — WorkflowClass Governance](09-workflow-class-governance.md) for the REST equivalent of the same lifecycle.
* [Chapter 15 — Known Limitations](15-known-limitations-and-gaps.md) for this and other verified gaps in one place.
