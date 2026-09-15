using FlowOS.Application.DTOs;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Domain.ValueObjects;
using MediatR;

namespace FlowOS.Application.Commands;

public sealed record CreateWorkflowContextBindingCommand(
    Guid TenantId,
    Guid SourceWorkflowClassId,
    string ContextType,
    string Name,
    WorkflowContextBindingDefinition Definition)
    : IRequest<WorkflowContextBindingDto>, IPolicySecuredCommand;

public sealed record UpdateWorkflowContextBindingCommand(
    Guid TenantId,
    Guid BindingId,
    WorkflowContextBindingDefinition Definition,
    Guid? SourceWorkflowClassId = null)
    : IRequest<WorkflowContextBindingDto>, IPolicySecuredCommand;

public sealed record ValidateWorkflowContextBindingCommand(
    Guid TenantId,
    Guid BindingId)
    : IRequest<WorkflowContextBindingValidationDto>, IPolicySecuredCommand;

public sealed record ActivateWorkflowContextBindingCommand(
    Guid TenantId,
    Guid BindingId)
    : IRequest<WorkflowContextBindingDto>, IPolicySecuredCommand;

public sealed record ArchiveWorkflowContextBindingCommand(
    Guid TenantId,
    Guid BindingId)
    : IRequest<WorkflowContextBindingDto>, IPolicySecuredCommand;

public sealed record ListWorkflowContextBindingsQuery(
    Guid TenantId,
    Guid? SourceWorkflowClassId = null) : IRequest<IReadOnlyList<WorkflowContextBindingDto>>;

public sealed record GetWorkflowContextBindingQuery(
    Guid TenantId,
    Guid BindingId) : IRequest<WorkflowContextBindingDto?>;
