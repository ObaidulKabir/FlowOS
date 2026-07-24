# 15. Known Limitations & Gaps

This chapter exists so this documentation set never overstates what FlowOS actually enforces today. Every item below is backed by a passing regression test — run `dotnet test FlowOS.sln` to reproduce them yourself.

## Policy `ConditionJson` evaluation (improved)

* **Previously**: `DefaultPolicyEvaluator` only denied when the policy name was exactly `"DenyAll"`, and `EfCorePolicyProvider` dropped `ConditionJson` when mapping EF entities to the domain `Policy` DTO.
* **Now**: domain `FlowOS.Security.Policies.Policy` carries `ConditionJson`; the provider maps it through; the evaluator denies when `ConditionJson` contains `{ "action": "Deny" }` (optional `reason`), and still supports the legacy `"DenyAll"` name. Malformed JSON fails closed (deny).
* **Still limited**: this is a minimal JSON contract, not a full rules engine (no day-of-week / role expressions yet). Extend `DefaultPolicyEvaluator` as product needs grow.
* **Proof**: `tests/FlowOS.UnitTests/Security/PolicyEvaluatorGapTests.cs` — now asserts ConditionJson is preserved and Deny actions are enforced.

## The runtime event-publishing path does not enforce State Machine rules

* **What the docs imply**: [Chapter 3](03-state-machines.md) documents `POST /api/statemachines/validate` as the authoritative legality check, and [Chapter 2](02-core-concepts.md) states "a workflow can never bypass a state machine".
* **What actually happens**: `WorkflowCommandHandlers.PublishEventCommand`'s handler calls `_engine.Advance()` **without** passing a `StateMachineDefinition` or the entity's current state. The `WorkflowEngine.Advance` method *supports* an optional state-machine-aware overload, but the live command handler doesn't use it — so publishing an event advances the *workflow* purely according to the workflow's own step transitions, even if a `StateMachineDefinition` exists for the entity type and would have denied the same transition.
* **Proof**: `tests/FlowOS.UnitTests/Application/Handlers/WorkflowCommandHandlers_StateMachineGapTests.cs` (2 tests) — a workflow advances via `PublishEventCommand` on an event that a corresponding State Machine, if consulted, would deny.
* **Practical implication**: the `/api/statemachines/validate` endpoint is currently a standalone advisory tool ([Chapter 3](03-state-machines.md#strategic-uses-of-the-validation-endpoint)) — call it explicitly from your client/agent before publishing an event if you need that legality guarantee today. Don't assume the engine consults it automatically during a normal `POST /api/events/publish` call.

## `AdminController` authorization

* `/api/admin/*` now requires `[Authorize(Roles = "Admin")]`. In development, send `X-Mock-Role: Admin`.
* `/api/roles` and `/api/policies` also require the `Admin` role (tenant security config is admin-only).
* Controllers that previously lacked `[Authorize]` (`Events`, `Agents`, `Tasks`, `StateMachines`, `Notifications`) now require authentication via the Mock (or real) scheme.
* Admin `config/publish` goes through `PublishConfigurationCommand` + `IConfigurationPublisher` (no DbContext in the controller).

## `WorkflowClassesController.ApproveAsPublic` has no admin-only check

* The [Chapter 9](09-workflow-class-governance.md) lifecycle describes "Approve" as an admin action, but `ApproveAsPublic` (`POST /api/workflow-classes/{id}/approve`) has no role/capability check in code beyond the controller-level `[Authorize]` (any authenticated caller, any role). Any tenant that can authenticate can promote a `Shared` class to `Public`.

## `WorkflowClassValidator` never actually checks "Law" (Workflow-vs-StateMachine legality)

* [Chapter 9](09-workflow-class-governance.md) previously implied that WorkflowClass validation enforces "the workflow cannot declare transitions the state machine doesn't permit." In the real source, `WorkflowClassValidator.Validate` has a `// 3. Law Validation (Workflow cannot bypass State Machine)` block whose body is entirely comments — it performs no additional check beyond the Consistency rules (`CON-001`/`CON-005`) that already confirm referenced events exist.
* This compounds the separate runtime gap above: a WorkflowClass can pass validation and even Publish successfully with a Workflow that is structurally fine but not a faithful projection of its own StateMachine, and that mismatch is also not caught when instances run.
* **Practical implication**: manually cross-check your Workflow's step transitions against your StateMachine's transitions when authoring — don't rely on `validate`/`publish` to catch a Workflow that quietly diverges from the Law it's supposed to obey.

## Three different, non-overlapping validation "error code" vocabularies exist in the docs and code

* **Vocabulary 1 — governance principles** (`SM-001`, `WF-002`, `EV-001`, `GOV-001`...): appeared in older internal design docs as if it were the live rule set. These were **never implemented as literal codes** — they're prose principles, now presented as such in [Chapter 13](13-mcp-and-ai-agent-integration.md#governance-principles-vs-actual-validator-codes--read-this-before-trusting-either-table).
* **Vocabulary 2 — the real validator** (`STR-*`, `WF-STR-*`, `CON-*`, `WF-COMP-*`, `WF-STRUCT-005`, `WF-VAL-*`, `EVT-SCHEMA-001`, `GOV-001`): what `WorkflowClassValidator.Validate` actually returns from `POST /api/workflow-classes/{id}/validate`, `.../publish`, `.../lint`, and the MCP `validate_draft_workflowclass` tool. This is the only vocabulary you'll ever see in a live API/tool response. Full table: [Chapter 9](09-workflow-class-governance.md#validation-rules--verified-against-workflowclassvalidatorvalidate-the-actual-error-codes-it-emits). The MCP `explain_validation_violation` tool now documents these same codes.
* ~~**Vocabulary 3 — stale MCP explain knowledge base**~~ — **fixed**: `AnalysisTools.ExplainValidationViolation` now recognizes Vocabulary 2 codes (`CON-*`, `STR-*`, `WF-COMP-*`, `GOV-001`, etc.).

## MCP "Read Gap" — partially closed, not fully

* As of this writing, MCP still has no `get_workflowclass(id)` tool to read back an existing (non-public) WorkflowClass, and no diagnostic/runtime-observability tools. Full detail: [Chapter 13](13-mcp-and-ai-agent-integration.md#gap-analysis--updated).

## Docker Compose does not publish the API's host port by default

* `docker-compose.yml`'s `flowos-api` service has no `ports:` mapping. `README.md` previously claimed `http://localhost:5005` works out of the box with `docker-compose up` — it does not, unless you add a port mapping (e.g. via a `docker-compose.override.yml`). See [Chapter 1](01-getting-started.md#option-b--run-the-full-stack-with-docker-compose).

## Inconsistent legacy port references (fixed in this rewrite)

* Older documentation in this repository referenced `http://localhost:5000`, `5001`, and `5005` inconsistently for local development. None of these match `src/FlowOS.Api/Properties/launchSettings.json`, which binds to **`5183`**. This entire guide has been standardized on `5183`, and it's also the port used by the working `clients/node-expense-client` demo (see [Chapter 16](16-sample-applications.md)).

## How to keep this chapter honest

If you fix one of the gaps above, please also delete (or move to a "Resolved" section) the corresponding entry here, and update the cross-references in the other chapters that mention it — search for the exception/gap name across `docs/`.
