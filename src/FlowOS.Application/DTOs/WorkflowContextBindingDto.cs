using FlowOS.Domain.ValueObjects;
using FlowOS.Domain.Validation;

namespace FlowOS.Application.DTOs;

public sealed record WorkflowContextBindingRevisionDto(
    Guid Id,
    int Revision,
    Guid SourceWorkflowClassId,
    string SourceWorkflowClassVersion,
    string Status,
    WorkflowContextBindingDefinition Definition,
    Guid? WorkflowDefinitionId,
    Guid? StateMachineDefinitionId,
    string? ContentHash,
    DateTime CreatedAtUtc,
    DateTime? ActivatedAtUtc,
    DateTime? SupersededAtUtc);

public sealed record WorkflowContextBindingDto(
    Guid Id,
    Guid TenantId,
    string ContextType,
    string Name,
    string Status,
    Guid? ActiveRevisionId,
    Guid? DraftRevisionId,
    WorkflowContextBindingRevisionDto? ActiveRevision,
    WorkflowContextBindingRevisionDto? DraftRevision,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ArchivedAtUtc);

public sealed record WorkflowContextBindingValidationDto(
    bool IsValid,
    IReadOnlyList<ValidationError> Errors);
