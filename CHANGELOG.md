# FlowOS Changelog

All notable changes to the FlowOS project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0-MVP] - 2026-09-02

### 🚀 Initial Minimum Viable Product (MVP) Release

FlowOS is an enterprise-grade Process Operating System that separates **State Authority (State Machines)** from **Process Orchestration (Workflows)**, **Business Logic (Policy Enforcement)**, and **AI Advisory (Agents & MCP)**.

### ✨ Key Features

#### 1. Core Engine & State Authority
- **Dual-Layer Kernel Architecture**: Strict separation between the State Machine engine (verifying legal entity state transitions) and the Workflow Orchestration engine (managing sequential/branching steps).
- **Multi-Tenancy by Design**: Isolated tenant boundary enforcement across workflows, state machines, tasks, events, and notifications.
- **Capability-Based RBAC**: Fine-grained capability checks (`PolicyEnforcementBehavior`) evaluated dynamically per request.

#### 2. Declarative Blueprint & Governance
- **100% Declarative JSON Schemas**: Blueprint authoring for workflows, state machines, events, roles, and capabilities.
- **Authoritative Static Validator (`WorkflowClassValidator`)**: Multi-layer static rule enforcement ensuring completeness, structural validity, reachability, and consistency.
- **Advisory Syntax Linter (`WorkflowJsonLinter`)**: Fast linting with line/column diagnostic warnings for IDEs and LLM assistants.

#### 3. Task Management & Human-in-the-Loop
- **Lifecycle API**: Complete endpoints for task querying, claiming, assignment, and completion with role gating.

#### 4. Transactional Outbox & Reliable Messaging
- **Outbox Pattern**: Atomic event persistence via EF Core `EventPublishingInterceptor`, guaranteeing at-least-once delivery even across transient network crashes.
- **Real-Time Notification Subsystem**: Event-driven notification projector with severity levels (Low, Normal, High) and read tracking.

#### 5. Persistent Timers, SLA Tracking & Automatic Escalation
- **Declarative Task-Level SLA**: Boundary SLA configuration (`Duration`, `TimeoutEvent`, `EscalationStepId`, `EscalationRole`, `IsInterrupting`).
- **Zero-Zombie Cancellation**: Automatic background cancellation of active timers upon early task completion or external state transition.
- **Persistent Timer Scheduler**: Fault-tolerant timer execution service capable of recovering across node restarts.

#### 6. Model Context Protocol (MCP) Server
- **Dual Transport Support**: Stdio for CLI/desktop agents and Streamable HTTP (`POST /mcp`) with API-key authentication for remote clients.
- **10 Design-Time Tools**: Tools for blueprint discovery, drafting, validation, linting, and design hints (`describe_workflowclass_schema`, `create_draft_workflowclass`, `explain_validation_violation`, `list_notifications`, etc.).

#### 7. Observability, Containerization & Health Checks
- **Health Checks**: Standard `/health/live` and `/health/ready` endpoints with database connectivity probes.
- **Production Container Packaging**: Multi-stage production `Dockerfile` configurations and `docker-compose.prod.yml`.

### 🧪 Automated Test Suite
- **188 / 188 passing automated tests** across `FlowOS.UnitTests`, `FlowOS.EndToEndTests`, and `FlowOS.MCP.UnitTests`.
