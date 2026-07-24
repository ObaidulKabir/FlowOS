# FlowOS Test Report

**Date:** 2026-07-24
**Status:** PASSED
**Scope:** Full solution build + test execution (Unit, Integration, E2E, MCP), plus targeted gap analysis and new regression tests.

## 1. Summary

Ran a full baseline build and test pass across the entire solution (17 projects, 3 test projects). Found and fixed a build-blocking file corruption issue, then added regression tests for two real architectural/behavioral gaps discovered during review. Final state: **154/154 tests passing, 0 failures, 0 build errors.**

## 2. Baseline Fix

- `tests/FlowOS.EndToEndTests/FlowOS.EndToEndTests.csproj` had a corrupted, stacked UTF-8 BOM (`EF BB BF` repeated ~10 times) at the start of the file, which broke `dotnet restore`/`build` for the **entire solution** (MSBuild XML parser error: "Data at the root level is invalid"). Rewrote the file with clean, valid XML content (no functional changes). This was blocking all builds and all test execution before the fix.

## 3. Test Execution Results (after fix)

| Project | Framework | Total | Passed | Failed | Skipped |
|---|---|---|---|---|---|
| `FlowOS.MCP.UnitTests` | net9.0 | 1 | 1 | 0 | 0 |
| `FlowOS.UnitTests` | net8.0 | 133 | 133 | 0 | 0 |
| `FlowOS.EndToEndTests` | net9.0 | 20 | 20 | 0 | 0 |
| **Total** | | **154** | **154** | **0** | **0** |

Build: 0 errors, 91 warnings (mostly nullable-reference-type warnings; catalogued but not all addressed - see Section 5).

## 4. New Tests Added

12 new tests added to close coverage gaps and lock in current (documented) behavior:

- **`Integration/PoliciesControllerTests.cs`** (6 tests) - No test coverage previously existed for `POST /api/policies` and `GET /api/policies/{id}`. Covers: create + return id, duplicate-name conflict within a tenant, same name allowed across different tenants, get by id, 404 for missing id, and tenant-isolation (a policy created under tenant A returns 404 when queried under tenant B).
- **`Application/Handlers/WorkflowCommandHandlers_StateMachineGapTests.cs`** (2 tests) - Documents that `PublishEventCommand`'s handler never loads or consults a `StateMachineDefinition`/entity state, even when one exists for the entity type. A sanity test proves the underlying `StateMachineEngine` *would* deny the transition if consulted; the second test proves the actual command handler advances the workflow anyway because it's never wired in.
- **`Security/PolicyEvaluatorGapTests.cs`** (4 tests) - Documents that `EfCorePolicyProvider` drops `ConditionJson` entirely when mapping DB policies to the domain `Policy` object, and that `DefaultPolicyEvaluator` only ever special-cases the exact string `"DenyAll"` - any other policy (regardless of its stored condition) is always allowed.

Additionally tightened an existing ambiguous assertion in `Integration/EventApiTests.cs` (`PublishEvent_WithMissingPermissions_ShouldFail`), which previously only asserted "not a success status code" (accepting either a 403 or an unhandled 500). Verified and now asserts the deterministic `403 Forbidden`, confirming `ApiExceptionFilterAttribute` correctly maps `PolicyViolationException` in the current codebase.

## 5. Known Gaps (documented via regression tests, not fixed)

These are pre-existing behaviors, now covered by tests so future changes are intentional and visible:

1. **State machine enforcement is not wired into the runtime event-publish path.** `WorkflowEngine.Advance` fully supports state-machine enforcement given a `StateMachineDefinition` and current entity state, and a standalone `POST /api/statemachines/validate` endpoint exists for dry-run checks - but `WorkflowCommandHandlers.Handle(PublishEventCommand, ...)` never loads either, so a state machine that would deny a transition has no effect on the real `/api/events/publish` call.
2. **Policy `ConditionJson` is not evaluated.** It's stored on the `Policy` entity and accepted by `POST /api/policies`, but `EfCorePolicyProvider` discards it when mapping to the domain policy object, and `DefaultPolicyEvaluator` only checks for the literal name `"DenyAll"`.
3. **Verbose debug logging via `Console.WriteLine`** remains in `EventsController`, `WorkflowCommandHandlers`, and `PolicyEnforcementBehavior` (dumps headers, roles, and resolved capabilities per request). Not a correctness issue, but noisy and not level-controlled; recommend migrating to `ILogger` with appropriate log levels.
4. **91 nullable-reference-type warnings** across `src/` and `tests/` (mostly `CS8602`/`CS8600`/`CS8603`/`CS8625`), plus a handful of xUnit analyzer and ASP0019 header-append warnings. None currently cause test failures; listed for future cleanup.

## 6. Conclusion

The solution's test suite is comprehensive and, after fixing the build-blocking corrupted project file, fully green (154/154). Coverage gaps around the Policies endpoint have been closed, and two real behavioral gaps (state machine enforcement not wired into publish, policy conditions not evaluated) are now documented with explicit regression tests rather than left as silent, undiscovered risk.
