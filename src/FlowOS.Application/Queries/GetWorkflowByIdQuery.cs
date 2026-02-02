using System;
using FlowOS.Application.DTOs;
using MediatR;

namespace FlowOS.Application.Queries;

public class GetWorkflowByIdQuery : IRequest<WorkflowSummaryDto?>
{
    public Guid TenantId { get; set; }
    public Guid Id { get; set; }
}
