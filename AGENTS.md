# AGENTS.md — rules for AI coding agents working on FlowOS

Read this file before every task. It applies to Cursor, Trae, Antigravity, Claude and any other agent.
Trae: switch on **"Include AGENTS.md in the context"** in settings, or this file is ignored.

## What FlowOS is

A .NET business-process engine: state machines ("Law"), workflows ("Work"), an event registry, a
HumanTask inbox, an MCP server for AI agents, and a React dashboard. Main projects:

| Folder | Purpose |
|--------|---------|
| `src/FlowOS.Domain`, `FlowOS.Events`, `FlowOS.StateMachines`, `FlowOS.Workflows` | Domain model and engines (no infrastructure) |
| `src/FlowOS.Core` | Shared models (outbox, idempotency), security rules, telemetry definitions |
| `src/FlowOS.Application` | Commands, queries, handlers (MediatR 12.x) |
| `src/FlowOS.Infrastructure` | EF Core/PostgreSQL, background services, integrations, migrations |
| `src/FlowOS.Api` | REST API host; `src/FlowOS.MCP` MCP host |
| `apps/dashboard` | React dashboard |
| `tests/` | Unit, MCP unit and end-to-end (Testcontainers PostgreSQL) tests |

## Never

1. Never commit secrets, passwords, API keys or tokens, not even as defaults or "examples" (`?? "password"`, `${VAR:-secret}`). Read them from configuration; fail clearly when missing.
2. Never add credentials that work in Production by default. Demo or test credentials must work only in Development (`TenantIdentityRules.DemoKeysAllowed`).
3. Never give a tenant rights over other tenants. Only the platform tenant is a platform administrator.
4. Never change behaviour that the task did not ask for. If you find a bug, report it in the PR description; fix it in a separate PR.
5. Never weaken, skip or delete a test to make a change pass.
6. Never push to `main`. Every push to `main` deploys to production automatically (`.github/workflows/deploy.yaml`).
7. Never commit logs, databases, archives, `tmp*`/`temp*` files or debug output (`scripts/check-repo-hygiene.sh` enforces this).
8. Never upgrade MediatR beyond 12.x (later versions are commercially licensed).
9. Never put personal data or raw tenant IDs into logs or telemetry. Use `FlowOsTelemetry.TenantHash`.
10. Never claim something in documentation or MCP guidance that the code does not do. Agents read the MCP guidance and act on it.

## Always

1. Work on one change request (CR) or issue per branch and PR. Put the ID in the PR title, for example `CR-06: optimistic concurrency on workflow instances`.
2. Write or update tests first. Run before finishing:
   `dotnet test tests/FlowOS.UnitTests && dotnet test tests/FlowOS.MCP.UnitTests && dotnet test tests/FlowOS.EndToEndTests`
3. Keep PRs small (under about 400 changed lines, excluding generated migrations).
4. Change the EF Core model only together with a migration: `dotnet ef migrations add <Name> -p src/FlowOS.Infrastructure -s src/FlowOS.Api`. `MigrationsUpToDateTests` fails on drift.
5. Use `TimeProvider` (or a time parameter) instead of `DateTime.UtcNow`/`DateTime.Now` in new or changed code.
6. Make retried writes idempotent (idempotency key) and keep every query tenant-scoped.
7. Update the documentation chapter and the MCP guidance (`FlowOsMcpGuidance.cs`) in the same PR as the behaviour they describe.
8. In C# raw string literals (`"""`), write real line breaks. `\n` is not an escape there and appears literally.
9. Describe the evidence in the PR: test output, and for runtime features a short log or screenshot.
10. If unsure what an API, package or setting does, check the code or ask. Do not invent it.

## Package and build facts

- .NET 10 LTS; target framework and package versions are central (`Directory.Build.props`, `Directory.Packages.props`). Add packages there, not in project files.
- PostgreSQL via Npgsql EF Core. In-memory database only in Development tests.
- Telemetry: `src/FlowOS.Core/Telemetry/FlowOsTelemetry.cs`; docs in `docs/28-observability.md`.

## Change requests

The FlowOS Modification Proposal lists CR-01 to CR-19 with scope and acceptance criteria. Paste the CR's
"Proposed change" and "Acceptance criteria" into the task, and do only that CR.
