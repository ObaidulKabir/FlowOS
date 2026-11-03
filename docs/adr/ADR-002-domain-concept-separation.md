# ADR-002 — Agent, Team, Task, and Workflow Are Separate Domain Concepts

**Status:** Accepted  
**Date:** 2026-11-03  
**Deciders:** Architecture  

## Context

Several terms in the proposal overlap with existing FlowOS entities. Without an explicit separation ruling, developers will corrupt existing infrastructure by re-using or extending the wrong aggregate.

## Decision

### Permanent design constraint: `Team` (IAM) ≠ `AgentTeam` (Orchestration)

`FlowOS.Domain.Entities.Team` is an **IAM role-hierarchy for human users**:
- Members have hierarchy levels (Member → Manager → Admin).
- Capabilities gate `iam.manage` checks.
- Mutations require `confirmHumanApproval`.
- Table name: `Teams`.
- Repository: `ITeamRepository`.

`FlowOS.Domain.Entities.Orchestration.AgentTeam` is a **multi-agent collaboration container**:
- Slots declare required AI agent capabilities.
- Lifecycle: Draft → Active → Archived.
- Table name: `AgentTeams` — **never** `Teams`.
- Repository: `IAgentTeamRepository` — **never** shares `ITeamRepository`.

These are permanently separate concepts. Merging them is a critical safety violation — IAM teams gate platform-wide mutations.

### Concept map

| Concept | Meaning | Entity |
|---|---|---|
| `AgentTeam` | Who collaborates (AI agents) | `FlowOS.Domain.Entities.Orchestration.AgentTeam` |
| `Team` | Who handles human work (IAM users) | `FlowOS.Domain.Entities.Team` |
| `AgentVersion` | What an agent is (immutable snapshot) | `FlowOS.Domain.Entities.Orchestration.AgentVersion` |
| `AgentTaskJob` | What work must be accomplished (workflow-scoped) | `FlowOS.Domain.Entities.AgentTaskJob` |
| `WorkflowInstance` | How execution is governed | `FlowOS.Workflows.Domain.WorkflowInstance` |
| `StateMachineDefinition` | Which transitions are permitted (Law) | `FlowOS.Domain.Entities.StateMachineDefinition` |
| `DomainEvent` | What actually happened | `FlowOS.Events.Models.DomainEvent` |

### Definition vs. execution split

Every aggregate follows the `WorkflowClass` → `WorkflowInstance` pattern:

| Definition (template) | Execution (runtime instance) |
|---|---|
| `AgentVersion` (immutable snapshot) | `AgentExecutionRecord` |
| `AgentTeam` (template) | `TeamExecution` (Phase 2) |
| `StrategicPlan` template | `StrategicPlan` instance (Phase 3) |

Definitions are never mutated after publication. Runtime changes produce new versions or new instances.

## Consequences

- Any PR that creates a new entity named `Team` (without the `Agent` prefix) in the orchestration namespace must be rejected.
- Any PR that adds orchestration-domain foreign keys to the `Teams` table must be rejected.
- Code reviewers should verify the table name `AgentTeams` on every migration that touches this domain.
