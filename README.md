# FlowOS

**Version 1.1.0**
*Enterprise Process Operating System*

FlowOS is a kernel-style process engine designed for correctness, compliance, and enterprise scale. It strictly separates **State Authority (State Machines)** from **Process Orchestration (Workflows)** and **Business Logic (Policy & Agents)**.

## 📚 Documentation

The full user guide lives in **[`docs/`](docs/README.md)** — a 16-chapter, example-driven guide covering everything from getting started to the API reference, all verified against the current codebase and the 154-test automated suite. Start there.

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
The API listens on **`http://localhost:5183`** (per `launchSettings.json` — see [Chapter 1](docs/01-getting-started.md) for why this, and not 5000/5001/5005, is correct).

### Running the full stack via Docker
```bash
docker-compose up -d --build
```
*   **API**: `flowos-api` (no host port is published by default in `docker-compose.yml` — see [Chapter 1](docs/01-getting-started.md#option-b--run-the-full-stack-with-docker-compose))
*   **Dashboard**: `http://localhost:3000`
*   **MCP server**: `flowos-mcp` (stdio JSON-RPC, no HTTP port)

The **Expense App** sample (`ExpenseApp/`) and the **Node.js demo client** (`clients/node-expense-client`) are documented in [Chapter 16 — Sample Applications](docs/16-sample-applications.md).

## 🧪 Testing

Run the full test suite (154 tests, in-memory DB, no Docker required):
```bash
dotnet test FlowOS.sln
```

## 🤖 AI Capabilities
FlowOS exposes a **Model Context Protocol (MCP)** server (`src/FlowOS.MCP`, stdio JSON-RPC) that lets AI agents author and validate `WorkflowClass` designs under strict, design-time-only governance, plus a runtime **Agent Insights API** (`/api/agents/insight`) for advisory, human-in-the-loop suggestions during live workflow execution. See [Chapter 13 — MCP & AI Agent Automation](docs/13-mcp-and-ai-agent-integration.md) and [Chapter 7 — AI Agents & Insights](docs/07-ai-agents-and-insights.md).
