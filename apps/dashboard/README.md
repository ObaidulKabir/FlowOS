# FlowOS Tenant Dashboard

This dashboard manages **WorkflowClass lifecycle and visibility only**.

## Governance Rules
1.  **UI Has No Authority**: All validation, authorization, and governance rules are enforced server-side.
2.  **Immutability**: Published/Shared/Public classes are read-only.
3.  **Tenant Isolation**: You only see your own classes and Public templates.

## Architecture
*   **Tech Stack**: React + TypeScript + Vite + Tailwind CSS.
*   **API**: Communicates with `flowos-api` via `/api/workflow-classes`.
*   **Docker**: Runs alongside the API in `docker-compose`.

## Development
```bash
cd apps/dashboard
npm install
npm run dev
```
(Ensure API is running on port 5005 locally, or use Docker)
