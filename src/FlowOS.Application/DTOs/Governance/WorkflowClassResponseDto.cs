using System;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;

namespace FlowOS.Application.DTOs.Governance;

public record WorkflowClassResponseDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public WorkflowClassScope Scope { get; init; }
    public WorkflowClassStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? PublishedAt { get; init; }
    public Guid? PreviousVersionId { get; init; } // Added
    public WorkflowClassBlueprint Definition { get; init; } = new();
}
