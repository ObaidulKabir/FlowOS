using System;
using System.Collections.Generic;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.DTOs.Orchestration;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;
using MediatR;

namespace FlowOS.Application.Commands.Orchestration;

// ── Write commands ─────────────────────────────────────────────────────────────

/// <summary>Create a new Draft plan template. ADR-006 Phase 4A.</summary>
public record CreateAgentWorkflowClassCommand(
    Guid TenantId,
    string Name,
    string Version,
    AgentWorkflowBlueprint Definition,
    Guid? PreviousVersionId = null,
    string? ChangeLog = null)
    : IRequest<AgentWorkflowClassResponseDto>, IPolicySecuredCommand;

/// <summary>Update the definition of a Draft plan. Throws if already Published.</summary>
public record UpdateAgentWorkflowClassDraftCommand(
    Guid TenantId,
    Guid Id,
    string Name,
    string Version,
    AgentWorkflowBlueprint Definition,
    string? ChangeLog = null)
    : IRequest<AgentWorkflowClassResponseDto>, IPolicySecuredCommand;

/// <summary>Publish a Draft, locking the blueprint and making the plan dispatchable.</summary>
public record PublishAgentWorkflowClassCommand(Guid TenantId, Guid Id)
    : IRequest<AgentWorkflowClassResponseDto>, IPolicySecuredCommand;

/// <summary>Deprecate a Published plan. Existing executions continue; no new ones start.</summary>
public record DeprecateAgentWorkflowClassCommand(
    Guid TenantId,
    Guid Id,
    string? Reason = null,
    Guid? MigrationTargetId = null)
    : IRequest<AgentWorkflowClassResponseDto>, IPolicySecuredCommand;

/// <summary>Abandon a Published or Deprecated plan (soft-delete). Idempotent.</summary>
public record AbandonAgentWorkflowClassCommand(Guid TenantId, Guid Id)
    : IRequest<AgentWorkflowClassResponseDto>, IPolicySecuredCommand;

/// <summary>
/// Hard-delete a Draft plan.
/// Guarded by GOV-DEL-001 (status must be Draft) and GOV-DEL-002 (no executions).
/// </summary>
public record DeleteAgentWorkflowClassCommand(Guid TenantId, Guid Id)
    : IRequest<Unit>, IPolicySecuredCommand;

/// <summary>
/// Validate a blueprint without storing it, so an author can check a draft before
/// publishing. Returns the full <c>ValidationResult</c> rather than throwing.
/// </summary>
public record ValidateAgentWorkflowBlueprintCommand(AgentWorkflowBlueprint Definition)
    : IRequest<FlowOS.Domain.Validation.ValidationResult>;

// ── Queries ────────────────────────────────────────────────────────────────────

/// <summary>Fetch a single plan by id, scoped to the tenant.</summary>
public record GetAgentWorkflowClassQuery(Guid TenantId, Guid Id)
    : IRequest<AgentWorkflowClassResponseDto?>;

/// <summary>List plans for the tenant, optionally filtered by status.</summary>
public record ListAgentWorkflowClassesQuery(
    Guid TenantId,
    WorkflowClassStatus? Status = null,
    int Skip = 0,
    int Take = 50)
    : IRequest<IReadOnlyList<AgentWorkflowClassResponseDto>>;

/// <summary>Return the lineage chain (newest first) from a given plan id.</summary>
public record GetAgentWorkflowClassLineageQuery(Guid TenantId, Guid Id, int MaxDepth = 10)
    : IRequest<IReadOnlyList<AgentWorkflowClassResponseDto>>;
