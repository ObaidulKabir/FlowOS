# Workflow Context Bindings

Workflow context bindings let one published workflow template serve multiple tenant business domains. The template owns process shape and conditions. A tenant binding adapts event names, roles, capabilities, entity type, and incoming payload fields without copying or mutating the template.

## Model

- `WorkflowClass`: published reusable template with canonical events, roles, conditions, and `contextSchema`.
- `WorkflowContextBinding`: stable tenant deployment identity, unique by context type and runtime name.
- `WorkflowContextBindingRevision`: draft or immutable active mapping. Every activation creates a new runtime version.
- `WorkflowDefinition` and `StateMachineDefinition`: tenant-owned runtime package materialized at activation.
- `WorkflowContextSnapshot`: canonical values for one instance. It pins the binding revision and stores no raw source payload by default.

Existing workflows remain valid. A start request with no context selector follows the legacy definition, class, and name resolution paths.

## Reusable approval template

Use a non-empty canonical state-machine entity type:

```json
{
  "contextSchema": "{\"type\":\"object\",\"required\":[\"Amount\",\"Description\",\"ApprovalLimit\"],\"properties\":{\"Amount\":{\"type\":\"number\"},\"Description\":{\"type\":\"string\"},\"ApprovalLimit\":{\"type\":\"number\"}}}",
  "events": [
    { "eventId": "EVT-SUBMIT", "name": "Submit" },
    { "eventId": "EVT-APPROVE", "name": "Approve" },
    { "eventId": "EVT-REJECT", "name": "Reject" }
  ],
  "stateMachine": {
    "entityType": "ApprovalSubject",
    "initialState": "Draft",
    "states": ["Draft", "Pending", "Approved", "Rejected"],
    "transitions": [
      { "fromState": "Draft", "toState": "Pending", "eventId": "EVT-SUBMIT" },
      {
        "fromState": "Pending",
        "toState": "Approved",
        "eventId": "EVT-APPROVE",
        "condition": "Amount <= ApprovalLimit"
      },
      { "fromState": "Pending", "toState": "Rejected", "eventId": "EVT-REJECT" }
    ]
  },
  "workflow": {
    "startStepId": "Submit",
    "steps": [
      {
        "stepId": "Submit",
        "stepType": "Command",
        "requiredRoles": ["Requester"],
        "nextSteps": { "EVT-SUBMIT": "Review" }
      },
      {
        "stepId": "Review",
        "stepType": "HumanTask",
        "requiredRoles": ["Approver"],
        "nextSteps": { "EVT-APPROVE": "END", "EVT-REJECT": "END" }
      }
    ]
  },
  "roles": [
    { "name": "Requester" },
    { "name": "Approver" }
  ],
  "capabilities": [
    { "code": "event.publish.EVT-SUBMIT" },
    { "code": "event.publish.EVT-APPROVE" },
    { "code": "event.publish.EVT-REJECT" }
  ]
}
```

Schema fields are JSON-encoded strings, matching existing event payload schemas. Conditions remain canonical. Bindings map source data into `Amount` and supply typed `ApprovalLimit`; they never rewrite the expression.

## Three independent bindings

Expense:

```json
{
  "sourceWorkflowClassId": "<template-id>",
  "contextType": "Expense",
  "name": "ExpenseApproval",
  "definition": {
    "entityType": "ExpenseEntity",
    "eventAliases": {
      "EVT-SUBMIT": "EVT-EXPENSE-SUBMIT",
      "EVT-APPROVE": "EVT-EXPENSE-APPROVE",
      "EVT-REJECT": "EVT-EXPENSE-REJECT"
    },
    "roleOverrides": { "Approver": "FinanceManager" },
    "inputMapping": {
      "Amount": "expense.amount",
      "Description": "expense.justification"
    },
    "conditionParameters": { "ApprovalLimit": 5000 }
  }
}
```

Leave:

```json
{
  "sourceWorkflowClassId": "<template-id>",
  "contextType": "Leave",
  "name": "LeaveApproval",
  "definition": {
    "entityType": "LeaveRequest",
    "roleOverrides": { "Approver": "HRManager" },
    "inputMapping": {
      "Amount": "request.days",
      "Description": "request.reason"
    },
    "conditionParameters": { "ApprovalLimit": 20 }
  }
}
```

Purchase order:

```json
{
  "sourceWorkflowClassId": "<template-id>",
  "contextType": "PurchaseOrder",
  "name": "PurchaseOrderApproval",
  "definition": {
    "entityType": "PurchaseOrder",
    "eventAliases": { "EVT-SUBMIT": "EVT-PO-SUBMIT" },
    "roleOverrides": { "Approver": "ProcurementManager" },
    "inputMapping": {
      "Amount": "order.total",
      "Description": "order.memo"
    },
    "conditionParameters": { "ApprovalLimit": 25000 }
  }
}
```

Mappings use `canonicalField -> source.path`. Only explicitly mapped fields enter the canonical snapshot; unmapped source fields are discarded, and an empty mapping does not retain the source payload. Only dotted object paths are supported in v1. Use a registered plugin for complex transformation.

## Lifecycle

1. Create a binding. Revision 1 is Draft.
2. Update the draft as needed.
3. Validate aliases, mappings, schemas, tenant roles, capabilities, and decision providers.
4. Activate. MCP requires explicit human confirmation, and the dashboard prompts before FlowOS atomically publishes a new workflow/state-machine/event package.
5. Update an active binding to create the next draft revision.
6. Activate again. New starts use the new revision; existing instances remain pinned to their old definition and snapshot.
7. Archive to block new starts. Existing instances continue.

Activation never changes the template, overwrites a runtime definition, creates tenant roles, or upgrades another binding.

## Business-context simulation

Use the tenant dashboard **Context Simulator** or `POST /api/context-bindings/simulate` to test the binding before activation and to verify the exact pinned runtime after activation.

- `revision: "draft"` validates the saved draft and compiles an ephemeral runtime package. It does not create workflow, state-machine, event, instance, snapshot, timer, outbox, or audit rows.
- `revision: "active"` loads the exact `WorkflowDefinition` and `StateMachineDefinition` IDs pinned to the active binding revision.
- Initial and event payloads use the same source schemas, aliases, global/event-specific mappings, immutable condition parameters, and canonical schema validation as production.
- Simulated roles participate in human-task and state-machine role checks. Tenant decision-provider bindings are resolved exactly as they are at runtime.
- A denied event is included in the trace, but its proposed canonical delta and transient instance changes are discarded.
- Webhooks, notifications, timers, lifecycle actions, and child workflows appear as **would execute** or pending work; none are dispatched or persisted.

Example:

```json
{
  "contextType": "Expense",
  "revision": "draft",
  "initialPayload": {
    "expense": {
      "amount": 1250,
      "justification": "Customer visit"
    }
  },
  "roles": ["FinanceManager"],
  "events": [
    {
      "eventType": "EVT-EXPENSE-SUBMIT",
      "payload": {}
    },
    {
      "eventType": "EVT-EXPENSE-APPROVE",
      "payload": {}
    }
  ],
  "maxSteps": 25
}
```

Specify exactly one of `contextBindingId` or `contextType`. The result includes revision lineage, the compiled workflow/state-machine graph, initial projection details, canonical before/delta/after values, every allowed or denied transition, planned external work, final context, and `sideEffectsSuppressed: true`.

`simulate_workflowclass` remains the template-design sandbox. Use `simulate_context_binding` when validating business vocabulary and source-to-canonical adaptation. Use `fork_workflow_simulation` after a real instance starts; replay/fork results preserve binding/revision and contextual/canonical event lineage and accept `simulatedRoles`.

SLA reminders on a waiting HumanTask or Command do not need a live clock. Agents should read MCP prompt `test_sla_reminders_in_simulator` or resource `flowos://guides/sla-reminder-simulation` before concluding the simulator is broken. A completing `nextSteps` event (`QUOTE_APPROVED`) causes the simulator to inject reminder `triggerEvent`s in duration order first; timeout must not fire. Set `autoAdvanceTimers: true` and omit the completing event to fire `TimeoutEvent` (`QUOTE_RESPONSE_OVERDUE`). Trace `[SLA Reminder Fired]` / `[SLA Timeout Fired]`. Put the timeout event on `nextSteps`. `isValid: true` plus simulate-to-Paid is the happy path, not a timeout proof.

A waiting step may also be acted by an agent. Put tenant-specific how-to-decide text on the binding `policyGuideline` and case fields on `inputMapping`; keep template how-to-decide on step `decisionGuideline`. Point `agentProvider` at a tenant `agent` plugin binding (the API key stays on the binding). Declare `agentTools` as resource plugins (`LookupRecord:<capability>`, `QueryRecords:`, `FetchDocument:`, `SearchKnowledge:`, `CheckPolicy:`) plus notify plugins and `capability:*` writes. FlowOS prefetches the reads; the model never sees tenant URLs. Auto-commit still uses `publish_event`. Read `design_agent_handled_step` / `flowos://guides/bounded-autonomy-tasks`. Do not use a Decision `Default` skip as the AI gate.

Decision auto-route (`conditions.Default` or `true`) moves `currentStep` without consuming a state-machine event. If the template still declares `Assigned + QUOTE_APPROVED → Quoted`, include `QUOTE_APPROVED` in the simulation event list. FlowOS applies it as state-only catch-up (step unchanged, state advances). Omitting it leaves state at Assigned and the next event is Denied. Bindings never create tenant roles: `simulate_context_binding` does not require tenant roles to exist, and a draft binding may point at a Draft workflow class. Do not publish a stripped-roles simulator variant. `validate_context_binding` / `activate_context_binding` still require a Published source and existing tenant roles (CTX-ROLE-002). Activation also fails if those tenant roles lack the remapped capabilities (CTX-CAP-003); bindings never auto-create roles or grants. Agents should read MCP resource `flowos://guides/dual-kernel-design` or prompt `design_dual_kernel_workflow`.

## MCP sequence

```json
{"name":"create_context_binding","arguments":{"tenantId":"<tenant>","sourceWorkflowClassId":"<template>","contextType":"Expense","name":"ExpenseApproval","definition":{"entityType":"ExpenseEntity","inputMapping":{"Amount":"expense.amount","Description":"expense.justification"},"conditionParameters":{"ApprovalLimit":5000}}}}
```

Then call:

- `validate_context_binding` with `id`
- `simulate_context_binding` with exactly one binding selector and `revision: "draft"`
- `activate_context_binding` with `id` and `confirmHumanApproval: true`
- `simulate_context_binding` with `revision: "active"` to verify the pinned runtime
- `start_workflow` with exactly one of `contextBindingId` or `contextType`
- `publish_event` with the contextual event ID

Example start:

```json
{
  "contextType": "Expense",
  "payload": {
    "expense": {
      "amount": 1250,
      "justification": "Customer visit"
    }
  },
  "businessReference": {
    "sourceSystem": "ERP",
    "externalEntityId": "EXP-1042"
  }
}
```

## REST API

- `POST /api/context-bindings`
- `PUT /api/context-bindings/{id}/draft`
- `POST /api/context-bindings/{id}/validate`
- `POST /api/context-bindings/{id}/activate`
- `POST /api/context-bindings/{id}/archive`
- `GET /api/context-bindings`
- `GET /api/context-bindings/{id}`
- `POST /api/context-bindings/simulate`
- `POST /api/workflows/start` with `contextBindingId` or `contextType`

The authenticated tenant always wins over a tenant value supplied by a client. Cross-tenant identifiers return not found.

## Security and provisioning

Role and capability overrides are references, not provisioning instructions. Create tenant roles and grant required capabilities before validation/activation. Bound human tasks expose all compiled `requiredRoles`, and completion requires role intersection. Activation and archival are high-risk MCP operations requiring human confirmation.

## Payload durability and replay

FlowOS validates the optional source schema, projects source fields, merges typed parameters, validates the template `contextSchema`, and stores only the canonical snapshot. Later events merge mapped deltas only after a successful transition. Task completions and timers load the same snapshot even without payload. Audit metadata records binding revision, canonical delta, canonical/contextual event IDs, and business reference so replay and fork simulation remain deterministic.

## Migration behavior

The migration is additive: it creates binding, revision, and snapshot tables; adds nullable lineage columns to workflow definitions; and persists state-transition constraints. Existing rows require no backfill. Legacy starts and existing instances are unchanged.

## v1 limits

- No expression rewriting or raw condition overrides.
- No automatic role/capability creation.
- No automatic upgrade when a template changes.
- No raw source-payload retention by default.
- No automatic nested subworkflow selection by context binding.
