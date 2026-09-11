# FlowOS

**Version 1.0.0-MVP**
*Enterprise Process Operating System*

FlowOS is a kernel-style process engine designed for correctness, compliance, and enterprise scale. It strictly separates **State Authority (State Machines)** from **Process Orchestration (Workflows)** and **Business Logic (Policy & Agents)**.

## 📚 Documentation

The full user guide lives in **[`docs/`](docs/README.md)** — a 16-chapter, example-driven guide covering everything from getting started to the API reference, all verified against the current codebase and the 188-test automated suite. See also **[`CHANGELOG.md`](CHANGELOG.md)** for release notes.

Quick links: [Getting Started](docs/01-getting-started.md) · [Core Concepts](docs/02-core-concepts.md) · [API Reference](docs/14-api-reference.md) · [Known Limitations](docs/15-known-limitations-and-gaps.md) · [Sample Applications](docs/16-sample-applications.md)

## 🚀 Getting Started

### Prerequisites
*   Docker & Docker Compose (optional — see [Chapter 1](docs/01-getting-started.md))
*   .NET 8 SDK
*   Node.js 20+ (for the dashboard / sample apps)

### Running locally (fastest)
```bash
cd src/FlowOS.Api
dotnet run --UseInMemoryDatabase=true
```
The API listens on **`http://localhost:5183`** (Health checks available at `/health/live` and `/health/ready`).

### Running the production stack via Docker Compose
```bash
# Copy production env template
cp .env.production.example .env.production

# Start API, MCP Server, and PostgreSQL
docker compose -f docker-compose.prod.yml up -d --build
```
*   **API**: `http://localhost:5183` (Health check: `http://localhost:5183/health/ready`)
*   **MCP server**: `http://localhost:8081` (`POST /mcp` Streamable HTTP or stdio)
*   **PostgreSQL**: `localhost:5432`

## 🧪 Testing

Run the full automated test suite (192 tests across MCP, Unit, and End-to-End integration):
```bash
dotnet test FlowOS.sln
```

## 🤖 MCP AI Control Plane
FlowOS is a **multi-tenant, state-machine-governed workflow control plane with an MCP interface for safe AI-agent interaction**. It exposes a standalone **Model Context Protocol (MCP)** server (`src/FlowOS.MCP`) supporting both **stdio** and **Streamable HTTP** (`MCP_TRANSPORT=http`, `GET /mcp` and `POST /mcp`):
* **Public Discovery (`GET /mcp`)**: Self-documenting endpoint returning interactive HTML or machine-readable JSON discovery metadata, tool schemas, tenant security semantics, risk ratings, and human confirmation requirements.
* **Protected Execution (`POST /mcp`)**: Authenticated JSON-RPC 2.0 interface exposing **21 tools** across workflow design, state machine verification, execution, human tasks, notifications, and advisory agents.
* **Enforced Agent Governance Policy**: High-risk, irreversible actions (such as `publish_workflowclass`) mandate explicit human confirmation (`confirmHumanApproval: true`), failing deterministically with `MCP-APPROVAL-REQUIRED` if omitted.
* **Object-Level Tenant Isolation & Anti-Enumeration**: Strict anti-BOLA/IDOR boundaries prevent foreign resource access, while normalizing foreign and non-existent IDs to `MCP-NOTFOUND-001` to eliminate enumeration oracles. Public blueprints (`Scope == Public`) remain safely executable cross-tenant with caller-isolated runtime state.

See [Chapter 13 — MCP & AI Agent Control Plane](docs/13-mcp-and-ai-agent-integration.md) and [Chapter 7 — AI Agents & Insights](docs/07-ai-agents-and-insights.md).
