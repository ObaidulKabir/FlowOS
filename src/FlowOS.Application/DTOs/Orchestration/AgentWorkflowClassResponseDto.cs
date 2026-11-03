using System;
using FlowOS.Domain;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;

namespace FlowOS.Application.DTOs.Orchestration;

/// <summary>
/// Read-model DTO for <see cref="FlowOS.Domain.Entities.Orchestration.AgentWorkflowClass"/>.
/// ADR-006 Phase 4A.
/// </summary>
public record AgentWorkflowClassResponseDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public WorkflowClassStatus Status { get; init; }
    public WorkflowClassScope Scope { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? PublishedAt { get; init; }
    public Guid? PreviousVersionId { get; init; }
    public string? ChangeLog { get; init; }
    public string? DeprecationReason { get; init; }
    public Guid? DeprecationMigrationTargetId { get; init; }
    public string FlowOsVersion { get; init; } = FlowOsRelease.Version;
    public AgentWorkflowBlueprint Definition { get; init; } = new();
}
