I will implement the **File-Author, DB-Execute** architecture for FlowOS configuration.

### **Plan:**

1.  **Define File Structure (Source of Intent)**:
    *   Create a `config/` directory in the project root.
    *   Create subdirectories: `events/`, `state-machines/`, `workflows/`.
    *   Add JSON schema files for validation.
    *   Add example configuration files (e.g., `order-approval.v1.json`, `EVT-ORDER-APPROVED.json`).

2.  **Implement Configuration Loader (Gatekeeper)**:
    *   Create a service `ConfigurationLoader` in `FlowOS.Infrastructure`.
    *   Implement validation logic (JSON Schema, Event Registry checks).
    *   Implement "Publish" logic: Read file -> Validate -> Insert into DB (if version doesn't exist).

3.  **Update Data Seeder (Migration)**:
    *   Modify `DataSeeder` to use `ConfigurationLoader` instead of hardcoded C# objects.
    *   This proves the "Author in File -> Execute in DB" flow works.

4.  **Verify Runtime (Execute)**:
    *   Ensure the engine still reads from the DB (which it already does).
    *   Verify the Admin API reflects the loaded configurations.

This establishes the correct lifecycle: **Author (Files) -> Validate (Loader) -> Publish (DB) -> Execute (Engine)**.