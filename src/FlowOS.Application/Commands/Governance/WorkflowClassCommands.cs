using System;
using System.Collections.Generic;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Validation;
using MediatR;

namespace FlowOS.Application.Commands.Governance;

public record CreateWorkflowClassCommand(Guid TenantId, string Name, string Version, WorkflowClassBlueprint Definition)
    : IRequest<WorkflowClassResponseDto>, IPolicySecuredCommand;

public record UpdateWorkflowClassCommand(Guid TenantId, Guid Id, string Name, string Version, WorkflowClassBlueprint Definition)
    : IRequest<WorkflowClassResponseDto>, IPolicySecuredCommand;

public record PublishWorkflowClassCommand(Guid TenantId, Guid Id)
    : IRequest<WorkflowClassResponseDto>, IPolicySecuredCommand;

public record SubmitWorkflowClassCommand(Guid TenantId, Guid Id)
    : IRequest<WorkflowClassResponseDto>, IPolicySecuredCommand;

public record WithdrawWorkflowClassCommand(Guid TenantId, Guid Id)
    : IRequest<WorkflowClassResponseDto>, IPolicySecuredCommand;

public record ValidateWorkflowClassCommand(Guid TenantId, Guid Id)
    : IRequest<ValidationResult>, IPolicySecuredCommand;

public record DeprecateWorkflowClassCommand(Guid TenantId, Guid Id)
    : IRequest<WorkflowClassResponseDto>, IPolicySecuredCommand;

public record DeleteWorkflowClassCommand(Guid TenantId, Guid Id)
    : IRequest<Unit>, IPolicySecuredCommand;

public record AbandonWorkflowClassCommand(Guid TenantId, Guid Id)
    : IRequest<WorkflowClassResponseDto>, IPolicySecuredCommand;

public record ApproveWorkflowClassCommand(Guid TenantId, Guid Id)
    : IRequest<WorkflowClassResponseDto>, IPolicySecuredCommand;

public record CopyWorkflowClassCommand(Guid TenantId, Guid Id, Guid NewTenantId)
    : IRequest<WorkflowClassResponseDto>, IPolicySecuredCommand;

public record CreateNewWorkflowClassVersionCommand(Guid TenantId, Guid Id)
    : IRequest<WorkflowClassResponseDto>, IPolicySecuredCommand;

public record LintWorkflowClassCommand(string JsonContent)
    : IRequest<IReadOnlyList<LintError>>;

