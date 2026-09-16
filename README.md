# FlowOS

**Version 1.0.0-MVP**
*Dual-kernel business automation operating system with an MCP control plane*

FlowOS is a business automation operating system: tenant work runs under dual-kernel Law (state machine) and Work (workflow), gated by capabilities and deny-only policy, with a HumanTask inbox, capability-bound integrations, a hosted DecisionPacket / autoCommit loop, side-effect-free simulation, and paid runtime entitlement. The claim is gated by [OS-1 Honesty Gate](docs/19-os-release-gate.md).

## 📚 Documentation

The full user guide lives in **[`docs/`](docs/README.md)** — a 19-chapter, example-driven guide covering everything from getting started to the API reference, all verified against the current codebase and the 369-test automated suite. See also **[`CHANGELOG.md`](CHANGELOG.md)** for release notes.

Quick links: [Getting Started](docs/01-getting-started.md) · [Core Concepts](docs/02-core-concepts.md) · [API Reference](docs/14-api-reference.md) · [Known Limitations](docs/15-known-limitations-and-gaps.md) · [OS-1 Honesty Gate](docs/19-os-release-gate.md) · [Sample Applications](docs/16-sample-applications.md)

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

Run the full automated test suite (369 tests: 303 unit, 28 end-to-end, and 38 MCP contract tests):
```bash
dotnet test FlowOS.sln
```

## 🤖 MCP AI Control Plane
FlowOS is a **multi-tenant, state-machine-governed workflow control plane with an MCP interface for safe AI-agent interaction**. It exposes a standalone **Model Context Protocol (MCP)** server (`src/FlowOS.MCP`) supporting both **stdio** and **Streamable HTTP** (`MCP_TRANSPORT=http`, `GET /mcp` and `POST /mcp`):
* **Public Discovery (`GET /mcp`)**: Self-documenting endpoint returning interactive HTML or machine-readable JSON discovery metadata, tool schemas, tenant security semantics, risk ratings, and human confirmation requirements.
* **Protected Execution (`POST /mcp`)**: Authenticated JSON-RPC 2.0 interface exposing **66 tools** across workflow design, context-aware simulation, context bindings, Copilot synthesis, time-travel replay, state machine verification, execution, human tasks, notifications, and advisory agents.
* **Enforced Agent Governance Policy**: High-risk actions (`publish_workflowclass`, `activate_context_binding`, and `archive_context_binding`) mandate explicit human confirmation (`confirmHumanApproval: true`), failing deterministically with `MCP-APPROVAL-REQUIRED` if omitted.
* **Object-Level Tenant Isolation & Anti-Enumeration**: Strict anti-BOLA/IDOR boundaries prevent foreign resource access, while normalizing foreign and non-existent IDs to `MCP-NOTFOUND-001` to eliminate enumeration oracles. Public blueprints (`Scope == Public`) remain safely executable cross-tenant with caller-isolated runtime state.

See [Chapter 13 — MCP & AI Agent Control Plane](docs/13-mcp-and-ai-agent-integration.md) and [Chapter 7 — AI Agents & Insights](docs/07-ai-agents-and-insights.md).
