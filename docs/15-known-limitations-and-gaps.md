# 15. Known Limitations & Gaps

This chapter exists so this documentation set never overstates what FlowOS actually enforces today. Every item below is backed by a passing regression test — run `dotnet test FlowOS.sln` to reproduce them yourself.

## Policy `ConditionJson` evaluation (improved)

* **Previously**: `DefaultPolicyEvaluator` only denied when the policy name was exactly `"DenyAll"`, and `EfCorePolicyProvider` dropped `ConditionJson` when mapping EF entities to the domain `Policy` DTO.
* **Now**: domain `FlowOS.Security.Policies.Policy` carries `ConditionJson`; the provider maps it through; the evaluator denies when `ConditionJson` contains `{ "action": "Deny" }` (optional `reason`), and still supports the legacy `"DenyAll"` name. Malformed JSON fails closed (deny).
* **Still limited**: this is a minimal JSON contract, not a full rules engine (no day-of-week / role expressions yet). Extend `DefaultPolicyEvaluator` as product needs grow.
* **Proof**: `tests/FlowOS.UnitTests/Security/PolicyEvaluatorGapTests.cs` — now asserts ConditionJson is preserved and Deny actions are enforced.

## Legacy instances without a workflow class are not checked against a State Machine

* **Class-backed instances are enforced.** Publishing an event to an instance created from a `WorkflowClass` goes through its State Machine ("Law"). If no usable Law is found, the request fails closed and the instance does not move.
* **Still open**: instances with an empty `WorkflowClassId` (created before class compilation existed) advance on the workflow graph alone, for backwards compatibility.
* **Proof**: `tests/FlowOS.UnitTests/Application/Handlers/WorkflowCommandHandlers_StateMachineGapTests.cs`: `Handle_PublishEventCommand_FailsClosed_WhenClassBackedInstanceHasNoUsableLaw` and `Handle_PublishEventCommand_LegacyEmptyClassId_MayStillAdvanceOnWorkflowGraph`.
* **Practical implication**: start new work from a published `WorkflowClass`. Migrate legacy definitions to classes if they must obey a State Machine.

## `WorkflowClassValidator` never actually checks "Law" (Workflow-vs-StateMachine legality)

* [Chapter 9](09-workflow-class-governance.md) previously implied that WorkflowClass validation enforces "the workflow cannot declare transitions the state machine doesn't permit." In the real source, `WorkflowClassValidator.Validate` has a `// 3. Law Validation (Workflow cannot bypass State Machine)` block whose body is entirely comments — it performs no additional check beyond the Consistency rules (`CON-001`/`CON-005`) that already confirm referenced events exist.
* This compounds the separate runtime gap above: a WorkflowClass can pass validation and even Publish successfully with a Workflow that is structurally fine but not a faithful projection of its own StateMachine, and that mismatch is also not caught when instances run.
* **Practical implication**: manually cross-check your Workflow's step transitions against your StateMachine's transitions when authoring — don't rely on `validate`/`publish` to catch a Workflow that quietly diverges from the Law it's supposed to obey.

## Three different, non-overlapping validation "error code" vocabularies exist in the docs and code

* **Vocabulary 1 — governance principles** (`SM-001`, `WF-002`, `EV-001`, `GOV-001`...): appeared in older internal design docs as if it were the live rule set. These were **never implemented as literal codes** — they're prose principles, now presented as such in [Chapter 13](13-mcp-and-ai-agent-integration.md#governance-principles-vs-actual-validator-codes--read-this-before-trusting-either-table).
* **Vocabulary 2 — the real validator** (`STR-*`, `WF-STR-*`, `CON-*`, `WF-COMP-*`, `WF-STRUCT-005`, `WF-VAL-*`, `EVT-SCHEMA-001`, `GOV-001`): what `WorkflowClassValidator.Validate` actually returns from `POST /api/workflow-classes/{id}/validate`, `.../publish`, `.../lint`, and the MCP `validate_draft_workflowclass` tool. This is the only vocabulary you'll ever see in a live API/tool response. Full table: [Chapter 9](09-workflow-class-governance.md#validation-rules--verified-against-workflowclassvalidatorvalidate-the-actual-error-codes-it-emits). The MCP `explain_validation_violation` tool now documents these same codes.
* ~~**Vocabulary 3 — stale MCP explain knowledge base**~~ — **fixed**: `AnalysisTools.ExplainValidationViolation` now recognizes Vocabulary 2 codes (`CON-*`, `STR-*`, `WF-COMP-*`, `GOV-001`, etc.).

## Workflow context binding v1 boundaries

* Conditions remain owned by the reusable template. Bindings can project canonical fields, add typed parameters, and select registered decision providers, but cannot rewrite expression strings.
* Input mappings support dotted object paths only. Complex transformation belongs in a plugin.
* **Business-context roles never touch FlowOS's own tenant Role/TenantUserRole tables, by design.** A `WorkflowClassBlueprint.roles[]` entry belongs to the modeled application, not to FlowOS's tenant/IAM security model (see [Chapter 8](08-security-roles-and-policies.md) vs. [Chapter 17 § Business-context roles](17-workflow-context-bindings.md#business-context-roles)). Activation compiles them onto `WorkflowDefinition.BusinessRoles` as declarative metadata; membership is resolved per running instance by `IBusinessRoleResolver`, not by creating a FlowOS role. This is not a gap to close — conflating the two was the bug. Proof: `tests/FlowOS.UnitTests/Security/RoleCapabilityFinalizationTests.cs`.
* Nested subworkflows do not inherit or automatically select context bindings. Child references remain concrete.
* Source business payloads are not retained by default; only the canonical snapshot and canonical audit deltas are durable.
* Template publication does not auto-upgrade bindings. Each binding revision is explicitly validated and activated.

## Resolved

Kept here so readers of older documents can see what changed.

* **State Machine enforcement on the event-publishing path (class-backed instances).** Earlier versions advanced instances on the workflow graph without consulting the State Machine. Class-backed instances now fail closed. See the open item above for legacy instances.
* **`AdminController` and security endpoints.** `/api/admin/*`, `/api/roles` and `/api/policies` require the `Admin` role. `Events`, `Agents`, `Tasks`, `StateMachines` and `Notifications` require authentication. Admin `config/publish` goes through `PublishConfigurationCommand`.
* **Promote-to-Public is limited to platform administrators.** `POST /api/workflow-classes/{id}/approve` first had no role check, then accepted the Admin of any tenant. A Public class is visible to every tenant, so only an Admin of the platform tenant may approve one. Proof: `tests/FlowOS.EndToEndTests/Governance/WorkflowClass_PublicApproval_Tests.cs`.
* **MCP schema examples.** Older MCP documents used `EventType`/`Label`/`Config` for step blueprints. The real fields are `EventId`, with no `Label` or `Config`. See [Chapter 9](09-workflow-class-governance.md).
* **Docker Compose API port.** `docker-compose.yml` maps the API to `5183` on the host.
* **Inconsistent local ports in older documents (`5000`, `5001`, `5005`).** All chapters use `5183`, the port in `src/FlowOS.Api/Properties/launchSettings.json`.

## How to keep this chapter honest

If you fix one of the gaps above, move its entry to the "Resolved" section in the same pull request, and update the cross-references in the other chapters that mention it — search for the exception/gap name across `docs/`.

The product-level scoreboard for "engine vs OS" is [Chapter 19 — OS-1 Honesty Gate](19-os-release-gate.md). Must-pass identity, dual-kernel fail-closed Law, admin-only promote-to-Public, role-filtered inbox, and hosted agent runtime are closed; remaining items here are later (`L-*`) polish, not GREEN blockers.
