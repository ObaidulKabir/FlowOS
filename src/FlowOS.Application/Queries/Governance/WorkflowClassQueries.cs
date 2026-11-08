using System;
using System.Collections.Generic;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Domain.Enums;
using MediatR;

namespace FlowOS.Application.Queries.Governance;

/// <param name="ApplicationId">Only this application's workflows.</param>
/// <param name="Unassigned">Only workflows not assigned to any application (ignored when ApplicationId is set).</param>
public record ListWorkflowClassesQuery(Guid TenantId, WorkflowClassScope? Scope, WorkflowClassStatus? Status, Guid? ApplicationId = null, bool Unassigned = false)
    : IRequest<List<WorkflowClassResponseDto>>;

public record GetWorkflowClassByIdQuery(Guid TenantId, Guid Id)
    : IRequest<WorkflowClassResponseDto?>;
