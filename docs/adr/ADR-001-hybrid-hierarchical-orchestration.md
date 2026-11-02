# ADR-001 — Hybrid Hierarchical Orchestration

**Status:** Accepted  
**Date:** 2026-11-03  
**Deciders:** Architecture  

## Context

FlowOS needs to support complex business objectives that require multiple AI agents working in coordination. Two pure approaches exist: static predefined teams (Model A) or fully dynamic assembly (Model B).

## Decision

Adopt **Model C: Hybrid Hierarchical Orchestration**.

The Agent Team Orchestrator is a first-class subsystem of FlowOS — not an independent agent framework bolted on alongside the platform.

### Three-level hierarchy

| Level | Name | Responsibility |
|---|---|---|
| 1 | Objective Orchestrator | Owns the complete objective; assembles teams; monitors inter-team progress |
| 2 | Team Orchestrators | Manage a specific workstream; decompose objectives; delegate to specialists |
| 3 | Specialist Agents | Execute narrowly scoped tasks; receive only the context and tools they need |

Nested sub-teams are permitted up to a **configurable maximum depth** (default: 1 level of nesting). This prevents unbounded agent recursion.

### Three control planes

| Plane | Responsibility |
|---|---|
| Intelligence | Planning, reasoning, agent selection, collaboration |
| Execution | Workflow scheduling, task dependencies, execution |
| Governance | State transitions, permissions, approvals, audit |

The Agent Team Orchestrator operates entirely in the Intelligence plane. It never crosses into the Governance plane — that remains FlowOS's exclusive responsibility (see ADR-003).

### Team composition

- Maintain **reusable predefined team templates** (`AgentTeam` entities with named `AgentTeamSlot`s).
- Allow the orchestrator to **dynamically instantiate, expand, shrink, or modify** team membership at runtime within governance policies.
- `AgentTeam` definition → `TeamExecution` runtime instance (mirrors the `WorkflowClass` → `WorkflowInstance` pattern).

## Consequences

- `AgentTeam` must support both template-level configuration and runtime override.
- The orchestrator must track which agent filled which slot, for audit and replay.
- Sub-team nesting depth must be enforced at orchestration time, not left to the LLM.
