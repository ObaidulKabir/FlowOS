# MCP & AI Agent Automation Support Review

**Date:** 2026-02-06
**Scope:** Review of Model Context Protocol (MCP) support for AI Agent automation in FlowOS.
**Documents Reviewed:** `docs/MCP_Usage_Examples.md`, `docs/UserManual.md` (Section 11).

## 1. Executive Summary

FlowOS currently implements a **Design-Time Only** MCP strategy. This aligns with the "Governance-First" architecture, where Agents are reasoning engines that propose changes but do not execute them. The existing documentation successfully defines the "Write" loop (Draft -> Validate -> Refine), but lacks comprehensive "Read" capabilities (Context Discovery) required for fully autonomous agents.

## 2. Existing Capabilities (Supported)

The current MCP implementation supports the following **Design & Governance** workflows:

| Capability | Tool Name (Example) | Description |
| :--- | :--- | :--- |
| **Tool Discovery** | `tools/list` | Agents can discover available operations dynamically. |
| **Draft Creation** | `create_draft_workflowclass` | Proposing new workflow blueprints from scratch. |
| **Validation** | `validate_draft_workflowclass` | Requesting authoritative validation from the FlowOS kernel. |
| **Refinement** | `update_draft_workflowclass` | Iterating on drafts based on validation errors. |
| **Template Reuse** | `fork_public_workflowclass` | Cloning public templates to a specific tenant. |

### Governance Constraints
The `UserManual.md` (Section 11) explicitly defines the boundaries:
*   **Proposals Only**: Agents output JSON artifacts, not side effects.
*   **No Runtime**: Agents cannot call `POST /api/workflows/start` or `POST /api/events`.
*   **Audit Trail**: All agent actions are design-time proposals subject to human or system approval.

## 3. Gap Analysis (Missing for Full Automation)

To support a truly autonomous "Senior Pair Programmer" agent, the following gaps in the MCP surface area were identified:

### A. Context Discovery ("The Read Gap")
Agents currently have no documented way to "see" the existing world before proposing changes.
*   **Missing**: `list_workflowclasses` - "What workflows do I already have?"
*   **Missing**: `get_workflowclass` - "Let me read the 'LeaveApproval' workflow before I modify it."
*   **Missing**: `get_policy_manifest` - "What are the security policies I need to adhere to?"

### B. Runtime Observability (Safe Read-Only)
While Agents shouldn't *act* on runtime, they often need to *observe* it to diagnose issues.
*   **Missing**: `get_workflow_instance_trace` - "Why did Workflow X fail?" (Diagnostic Agent).
*   **Missing**: `search_event_log` - "Find patterns in recent failures."

### C. Knowledge Retrieval
*   **Missing**: `search_docs` - Access to the `UserManual.md` or `InvariantManifest.md` via MCP to ground reasoning in project laws.

## 4. Recommendations

To upgrade FlowOS from "Agent-Assisted" to "Agent-Automated", we recommend implementing the following MCP Resources and Tools:

### Phase 1: Context Awareness (High Priority)
Add "Read" tools to complete the loop.
1.  **Tool**: `list_workflow_definitions(tenantId)`
2.  **Tool**: `get_workflow_blueprint(id)`
3.  **Resource**: `flowos://policies/active` (Read active policies as a resource)

### Phase 2: Diagnostic Support (Medium Priority)
Allow agents to debug the system without touching it.
1.  **Tool**: `get_instance_history(instanceId)`
2.  **Tool**: `explain_validation_error(errorCode)`

### Phase 3: Documentation Access
Expose documentation as MCP Resources.
1.  **Resource**: `flowos://docs/invariants`
2.  **Resource**: `flowos://docs/api`

## 5. Conclusion

The current MCP support is robust for **Greenfield Design** (creating new things) but insufficient for **Brownfield Maintenance** (fixing/updating existing things). Closing the "Read Gap" is the single most effective step to enable high-value automation scenarios.
