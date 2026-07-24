# 16. Sample Applications

FlowOS ships three real, runnable examples that exercise the API end-to-end. Reading their source is one of the fastest ways to see the concepts in this guide used in a real client.

## `clients/node-expense-client` — minimal CLI client

A tiny Node.js CLI (`axios` + `readline`) that walks through the entire lifecycle of an `ExpenseApproval` workflow interactively: start → submit → approve/reject → final status, printing the timeline at each step.

```bash
cd clients/node-expense-client
npm install
npm start
```

It talks to `http://localhost:5183/api` (tenant `22222222-2222-2222-2222-222222222222`, role `Admin`) — this is the **verified, correct** local dev URL (see [Chapter 15](15-known-limitations-and-gaps.md#inconsistent-legacy-port-references-fixed-in-this-rewrite)). Good starting point if you want the smallest possible working example of [Chapter 5](05-workflows-and-versioning.md) and [Chapter 4](04-events-and-registry.md) in one file — read `index.js`, it's under 110 lines.

## `ExpenseApp/` — full-stack demo (Express + SQLite backend, React + Vite frontend)

A more realistic sample app: a small expense-tracking UI backed by its own Express/SQLite service, which in turn drives a `WorkflowClass`-based approval process on FlowOS.

* **`ExpenseApp/backend`** (`server.js`, port `3001`) — owns its own local `expenses.db` (SQLite) for UI-friendly data (amount, description, status), and calls the FlowOS API to drive the actual approval process:
  * Discovers the right `WorkflowClass` by name at startup (prefers `ExpenseApprovalV2`, falls back to `ExpenseApproval`) via `GET /api/workflow-classes`, then starts instances with `workflowClassId` directly (`POST /api/workflows/start`).
  * Implements real business logic client-side to decide **which event** to publish: e.g. on approval, `amount > 100` publishes `EVT-ESCALATE` (routing to `PendingDirector`) instead of `EVT-APPROVE` directly. This is exactly the [Chapter 6](06-human-tasks-and-decisions.md) pattern — the client expresses intent (which outcome), FlowOS's Workflow/State Machine decide if the resulting transition is legal.
  * Fetches full audit history for an expense via `GET /api/admin/workflows/{id}` ([Chapter 14](14-api-reference.md#5-admin--admincontroller-apiadmin--read-only--config-publish)).
* **`ExpenseApp/frontend`** — a React + TypeScript + Vite app (currently mostly the default Vite template plus `ExpenseList`/`ExpenseForm` components) that talks to the backend above.

```bash
cd ExpenseApp/backend && npm install && npm start   # http://localhost:3001
cd ExpenseApp/frontend && npm install && npm run dev # http://localhost:3002 (per docker-compose.test.yaml) or Vite's default port locally
```

Or via Docker: `docker-compose.test.yaml` runs both as `expense-backend`/`expense-frontend` behind Traefik on a real domain — not needed for local development.

## `apps/dashboard` — Tenant Dashboard (governance UI)

A React + TypeScript + Vite + Tailwind app whose **entire purpose is `WorkflowClass` lifecycle and visibility** ([Chapter 9](09-workflow-class-governance.md)) — nothing else. Its own README states the governing rule plainly: *"This dashboard manages WorkflowClass lifecycle and visibility only."*

* Talks to `flowos-api` via `/api/workflow-classes`.
* **No business logic lives in the dashboard.** All validation, authorization, and governance rules are enforced server-side; the UI is read/observe/act-through-API only, never a shortcut around the state machine.
* Published/Shared/Public WorkflowClasses are rendered read-only — matching the immutability rules in [Chapter 9](09-workflow-class-governance.md#lifecycle).

```bash
cd apps/dashboard
npm install
npm run dev
```

Ensure the API is reachable (locally on `5183`, or via Docker where it's proxied as `flowos-api:8080`).

## Summary table

| App | Stack | Talks to FlowOS via | Best chapter to read alongside |
|---|---|---|---|
| `clients/node-expense-client` | Node.js CLI | Runtime API (`/api/workflows`, `/api/events`) | [Chapter 5](05-workflows-and-versioning.md) |
| `ExpenseApp/backend` + `frontend` | Express/SQLite + React/Vite | Runtime API + `/api/workflow-classes` (read) + `/api/admin/workflows` (history) | [Chapter 6](06-human-tasks-and-decisions.md) |
| `apps/dashboard` | React/Vite/Tailwind | `/api/workflow-classes` (full CRUD/lifecycle) | [Chapter 9](09-workflow-class-governance.md) |
