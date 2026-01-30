I will confirm and enforce the persistent database implementation. The current `Program.cs` logic falls back to InMemory if no connection string is found, which is risky for production/dev consistency. The user wants InMemory *only* for unit tests.

### Plan
1.  **Modify Program.cs**:
    *   Remove the implicit fallback to InMemory database.
    *   If `DefaultConnection` is present, use `UseNpgsql`.
    *   If `DefaultConnection` is missing, throw an exception (forcing the developer to configure the DB), *unless* the environment is explicitly "Testing" (though typically Unit Tests configure their own context, so throwing in `Program.cs` is safer).
    *   *Correction*: Unit tests typically use `WebApplicationFactory` which replaces services. So `Program.cs` should ideally fail fast if DB config is missing in a real run.
2.  **Update appsettings.Development.json**:
    *   Add a valid PostgreSQL connection string pointing to `localhost` (for running via `dotnet run` outside Docker).
3.  **Verify**:
    *   Check `Program.cs` logic.
    *   Check `appsettings.json` / `appsettings.Development.json`.

### Rationale
By removing the fallback, we ensure that:
*   `dotnet run` fails (or connects to real DB) instead of silently using InMemory.
*   Docker works (connection string injected).
*   Unit Tests work (they replace the DbContext registration anyway).

### Modified Files
*   `src/FlowOS.API/Program.cs`
*   `src/FlowOS.API/appsettings.Development.json`