# FlowOS Invariant Manifest

This document tracks the non-negotiable architectural rules of FlowOS. Any change that violates these invariants is considered a regression.

## Core Kernel Invariants
1.  **Law First**: State Machines define legality. Workflows cannot execute transitions not permitted by the State Machine.
2.  **Event = Truth**: All state changes must be the result of a Domain Event.
3.  **Tenant Isolation**: Data and execution must never leak across tenant boundaries.

## Governance Invariants
1.  **Configuration ≠ Authority**: `WorkflowClass` is configuration only. It never executes directly and never grants runtime authority.
2.  **Immutability**: Once Published, a Workflow Version is immutable.
3.  **Server-Side Validation**: All lifecycle transitions (Draft -> Published -> Shared) must be validated by the server. UI checks are advisory only.
4.  **Dashboard Scope**: Tenant dashboards manage `WorkflowClass` lifecycle and visibility only. They cannot mutate runtime behavior or bypass validation.
5.  **UI ≠ Authority**: UI is governance-only; all authority lives server-side. The dashboard must not implement business rules or simulate lifecycle transitions.
6.  **Public Templates**: Public `WorkflowClasses` are read-only templates. They must be copied (with a new TenantId and reset Version) before execution.
