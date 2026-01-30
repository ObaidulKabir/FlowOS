using System;
using System.Collections.Generic;
using FlowOS.Application.DTOs;
using FlowOS.Workflows.Enums;
using MediatR;

namespace FlowOS.Application.Queries;

public class GetWorkflowsQuery : IRequest<List<WorkflowSummaryDto>>
{
    public Guid TenantId { get; set; }
    public WorkflowInstanceStatus? Status { get; set; }
}
