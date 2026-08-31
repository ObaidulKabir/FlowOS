# 1. Getting Started

## Prerequisites

* .NET 8 SDK (the API/Domain/Workflows projects target `net8.0`; the MCP and E2E test projects target `net9.0` — install both SDKs, or the latest .NET SDK which can build both via multi-targeting)
* Docker & Docker Compose (optional, for the full stack with PostgreSQL, the dashboard, and the Expense sample app)
* Node.js 20+ (only needed for the dashboard, the Expense sample app, or the Node.js demo client)

## Option A — Run the API locally with `dotnet run` (fastest)

```bash
cd src/FlowOS.Api
dotnet run
```

The project's `Properties/launchSettings.json` binds the HTTP profile to:

```
http://localhost:5183
```

> **Important — verified port.** Older documentation in this repository referenced `5000`, `5001`, and `5005` inconsistently. None of those match the current `launchSettings.json`. **`5183` is the correct, current local dev port.** All examples in this guide use it.

By default `Program.cs` requires a PostgreSQL connection string (`ConnectionStrings:DefaultConnection`). For a zero-dependency local run, use the in-memory database switch:

```bash
dotnet run --UseInMemoryDatabase=true
```

On startup, `DataSeeder` seeds a default tenant (`11111111-1111-1111-1111-111111111111`) and loads every JSON configuration under `flowos-config/` (events, state machines, workflows) via `ConfigurationLoader`.

## Option B — Run the full stack with Docker Compose

```bash
docker-compose up -d --build
```

This starts four services defined in `docker-compose.yml`:

| Service | Image/Build | Notes |
|---|---|---|
| `flowos-api` | `src/FlowOS.Api/Dockerfile` | Connects to the `postgres` service. **No host port is published in the base `docker-compose.yml`** — add a `ports:` mapping (e.g. `"5183:8080"`) in a `docker-compose.override.yml` if you need to reach it from your host, or use `docker-compose exec`/the dashboard's proxy. |
| `flowos-dashboard` | `docker/dashboard.Dockerfile` | Vite dev server on port `3000`, proxies API calls to `flowos-api:8080` |
| `flowos-mcp` | `src/FlowOS.MCP/Dockerfile` | Streamable HTTP MCP (`POST /mcp` on port 8080; set `MCP_TRANSPORT=stdio` for local Cursor stdio) |
| `postgres` | `postgres:15-alpine` | Database `flowos`, user/password `postgres`/`password` |

> There is also a `docker-compose.test.yaml` at the repo root. Despite the name, it is **not** a lightweight test harness — it's a production-style deployment manifest that fronts every service (including the `ExpenseApp` sample) with a Traefik reverse proxy on a real domain. Don't use it for local development.

## Mock authentication (development only)

There is no real identity provider wired up yet. `MockAuthMiddleware` (`src/FlowOS.Api/Middleware/MockAuthMiddleware.cs`) turns two headers into a `ClaimsPrincipal`:

| Header | Purpose |
|---|---|
| `x-tenant-id` | The tenant GUID. Read by `ICurrentUser.TenantId` on every request. |
| `X-Mock-Role` | Simulates a logged-in role (e.g. `Manager`, `Admin`). Without this header, the request is anonymous and any `[Authorize]`-protected endpoint (e.g. `/api/workflows`, `/api/workflow-classes`) returns `401 Unauthorized`. |

Every curl example in this guide sends both headers:

```bash
curl -X GET "http://localhost:5183/api/workflows" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -H "X-Mock-Role: Admin"
```

## Your first API call

Check the seeded workflow catalog is loaded:

```bash
curl -X GET "http://localhost:5183/api/admin/state-machines" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111"
```

Start a workflow and immediately check its state (see [Chapter 5](05-workflows-and-versioning.md) for the full walkthrough):

```bash
curl -X POST "http://localhost:5183/api/workflows/start" \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: 11111111-1111-1111-1111-111111111111" \
  -H "X-Mock-Role: Admin" \
  -d '{
    "tenantId": "11111111-1111-1111-1111-111111111111",
    "workflowName": "OrderApprovalWorkflow",
    "version": 1
  }'
```

## Running the test suite

```bash
dotnet test FlowOS.sln
```

This runs three test projects: `FlowOS.UnitTests` (133 tests), `FlowOS.EndToEndTests` (20 tests), and `FlowOS.MCP.UnitTests` (1 test) — 154 tests total, all using an in-memory database, so no Docker/PostgreSQL is required.

## What's next

* New to the mental model? Read [Chapter 2 — Core Concepts](02-core-concepts.md).
* Want to see real sample apps talking to the API? Read [Chapter 16 — Sample Applications](16-sample-applications.md).
