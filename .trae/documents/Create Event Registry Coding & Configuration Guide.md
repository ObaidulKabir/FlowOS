I will create a comprehensive **Event Registry Guide** (`EVENT_REGISTRY_GUIDE.md`) in the project root to standardize how developers should register and consume events.

### **Proposed Content Structure:**

1.  **Core Concept**:
    *   Explanation of **Event IDs** (e.g., `EVT-ORDER-APPROVED`) vs. Legacy Strings.
    *   The "Single Event, Dual Consumption" principle (Workflow + State Machine).

2.  **Registration Guide**:
    *   **Naming Convention**: `EVT-{ENTITY}-{ACTION}` (Uppercase, dash-separated).
    *   **How to Register**: Code examples for adding `EventDefinition` entries via `DataSeeder` (since no UI exists yet).
    *   **Categories**: When to use `Decision`, `System`, `Human`, or `Agent`.

3.  **Configuration Guide (JSON/Code)**:
    *   **Workflows**: How to map `NextSteps` using Event IDs.
    *   **State Machines**: How to use the new `eventId` field in transitions.
    *   **Example**: Side-by-side comparison of "Old vs. New" configuration.

4.  **Runtime Behavior**:
    *   Validation rules: Why `EVT-` strings *must* exist in the registry.
    *   Legacy fallback support (Phase 1).

5.  **Migration Checklist**:
    *   Steps to migrate existing string-based events to the Registry.

I will create this file to ensure the team follows the new "Configuration-First" invariants.