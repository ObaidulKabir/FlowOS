# FlowOS

**Version 1.1.0**
*Enterprise Process Operating System*

FlowOS is a kernel-style process engine designed for correctness, compliance, and enterprise scale. It strictly separates **State Authority (State Machines)** from **Process Orchestration (Workflows)** and **Business Logic (Policy & Agents)**.

## 📚 Documentation

### Core Guides
*   **[User Manual](docs/UserManual.md)**: The authoritative guide to FlowOS concepts (Law, Work, Truth).
*   **[API Documentation](API_DOCUMENTATION.md)**: REST API reference.
*   **[Event Registry Guide](EVENT_REGISTRY_GUIDE.md)**: How to define and manage domain events.

### New Features (v1.1)
*   **[Payload Evaluation](docs/PayloadEvaluation.md)**: Using event data to drive workflow decisions (e.g., `Amount > 1000`).
*   **[AI Agent Integration](docs/AgentIntegrationStrategy.md)**: How agents provide insights and **Suggested Actions** to human users.
*   **[MCP Support](docs/FlowOS%20Design%20MCP%20Support.md)**: Tools for AI agents to reason about and validate workflow designs.

## 🚀 Getting Started

### Prerequisites
*   Docker & Docker Compose
*   .NET 8.0 SDK
*   Node.js 20+

### Running the Stack
```bash
docker-compose up -d --build
```

*   **API**: `http://localhost:5005`
*   **Dashboard**: `http://localhost:3000`
*   **Expense App**: `http://localhost:3002`

## 🧪 Testing

Run the full test suite:
```bash
dotnet test
```

## 🤖 AI Capabilities
FlowOS exposes a **Model Context Protocol (MCP)** service at `stdio` (when running `FlowOS.MCP`) or via the Agent API, allowing external AI agents to:
1.  **Validate** workflow designs against the constitution.
2.  **Suggest** next steps based on business data.
3.  **Analyze** risk and compliance in real-time.
