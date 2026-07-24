using System;
using System.Collections.Generic;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Domain.Enums;
using MediatR;

namespace FlowOS.Application.Queries.Governance;

public record ListWorkflowClassesQuery(Guid TenantId, WorkflowClassScope? Scope, WorkflowClassStatus? Status)
    : IRequest<List<WorkflowClassResponseDto>>;

public record GetWorkflowClassByIdQuery(Guid TenantId, Guid Id)
    : IRequest<WorkflowClassResponseDto?>;
