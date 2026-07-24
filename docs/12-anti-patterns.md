# 12. Anti-Patterns

How **NOT** to use FlowOS.

## ❌ "God Mode" client

**Don't do this:**

```javascript
if (userApproved) {
  flow.transitionTo("EndStep"); // WRONG: client deciding state
}
```

**Do this:**

```javascript
if (userApproved) {
  flow.publishEvent("EVT-DESIGN-APPROVED"); // OK: client expressing intent, FlowOS decides
}
```

Your client never "sets" a step or state directly. It only ever expresses that something happened, via an event or a task completion. See [Chapter 2 — Core Concepts](02-core-concepts.md#clients-react-flowos-decides).

## ❌ Hardcoded step-to-UI logic

**Don't do this:**

```javascript
if (currentStep === "DesignTask") {
  showDesignForm();
} else if (currentStep === "Review") {
  showReviewForm();
}
```

**Why it's wrong:** if the workflow definition changes (e.g. `"Review"` is renamed or split into two steps), your UI silently breaks with no compile-time warning.

**Do this instead:** query task metadata and map Step IDs to UI components dynamically, or use generic task handlers wherever possible.

## ❌ Ignoring policies

**Don't do this:** assume that because a button is visible, the underlying action will succeed.

**Do this:** handle `403 Forbidden` / policy violations gracefully in the UI — e.g. "Action blocked by policy: Weekend Freeze" — and, where possible, pre-check legality via the [State Machine validation endpoint](03-state-machines.md#validating-a-transition-via-the-api) before rendering the action as available.

## ❌ Merging Law and Work (state-as-step / step-as-state)

**Symptom:** State names like `"PendingManagerApproval"` (a task description masquerading as a legal state), or a Workflow step that assumes it is legal simply because it exists in the graph.

**Why it's wrong:** it blurs the Iron Triangle described in [Chapter 2](02-core-concepts.md#the-iron-triangle-law-work-truth). Legality must always be explicit in the State Machine, never inferred from workflow position.

## ❌ Command-style events

**Symptom:** `EVT-ApproveLeave`, `EVT-StartProcess`.

**Why it's wrong:** Events must represent completed **facts**, not instructions. Prefer `EVT-LEAVE-APPROVED`, `EVT-PROCESS-STARTED` — see the naming convention in [Chapter 4](04-events-and-registry.md#naming-convention).

## ❌ Treating a `WorkflowClass` Draft as authoritative

**Symptom:** a client-side tool (or an AI agent) assumes a Draft or even a successfully-validated design is safe to rely on operationally.

**Why it's wrong:** validation success never implies publish/execution authority — see [Chapter 9](09-workflow-class-governance.md#lifecycle) and [Chapter 13](13-mcp-and-ai-agent-integration.md#governance-constitution-summary). Only an explicit `Publish` action by an authorized actor makes a design executable.

## Where to go next

* [Chapter 15 — Known Limitations](15-known-limitations-and-gaps.md) — a few of these "anti-patterns" are, today, technically possible because enforcement hasn't fully caught up with the architecture's intent. Read that chapter so you don't accidentally rely on a gap.
