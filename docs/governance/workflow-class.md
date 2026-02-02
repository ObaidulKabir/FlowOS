# WorkflowClass Governance

WorkflowClass is the atomic unit of authoring, governance, and sharing in FlowOS. It represents a versioned configuration pack that compiles into runtime executable definitions.

## Core Principles
1.  **Configuration ≠ Authority**: WorkflowClass is configuration only. It never executes directly.
2.  **Immutability**: Once published, a version is immutable.
3.  **Validation**: Server-side validation is authoritative.

## Lifecycle
The lifecycle is strictly enforced:

1.  **Draft**: Editable, not executable. Visible only to tenant.
2.  **Published (Private)**: Immutable. Can be compiled to runtime. Visible only to tenant.
3.  **Shared**: Submitted for admin review. Read-only.
4.  **Public**: Approved global template. Visible to all tenants. Must be copied to execute.
5.  **Deprecated**: No new instances/copies.

## Scopes
*   **Private**: Tenant-specific logic.
*   **Shared**: Staging area for public review.
*   **Public**: Reusable templates (no tenant secrets allowed).

## Validation Rules
Every state transition triggers validation:
*   **Structural**: Required fields, naming.
*   **Internal Consistency**: All references (Events, States, Steps) must resolve within the pack.
*   **Law**: Workflow steps must adhere to State Machine transitions.
*   **Governance**: Roles and Capabilities must be declared.

## Adoption
To use a **Public** WorkflowClass:
1.  Create a **Copy** (Assigns new TenantId, resets to v1 Draft).
2.  Customize (if needed).
3.  Publish (Private).
4.  Compile/Deploy to Runtime.
