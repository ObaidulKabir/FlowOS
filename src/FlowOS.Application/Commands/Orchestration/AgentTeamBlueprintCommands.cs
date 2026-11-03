using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Validation;
using MediatR;

namespace FlowOS.Application.Commands.Orchestration;

// ─────────────────────────────────────────────────────────────────────────────
// ADR-007 §8: AgentTeamBlueprint authoring, starting with validation.
//
// No create/update/publish commands yet — there is no AgentTeamClass entity to store
// a blueprint in. This mirrors how AgentWorkflowBlueprint's own validator shipped before
// its CRUD lifecycle; persistence, versioning, and compilation to AgentTeam follow once
// the ADR's own "Open decision for review" (AgentTeamStatus vs. blueprint lifecycle) is
// settled.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Check an AgentTeamBlueprint without storing it — there is nowhere to store one yet.</summary>
public record ValidateAgentTeamBlueprintCommand(AgentTeamBlueprint Definition)
    : IRequest<ValidationResult>;
