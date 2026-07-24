using System;
using System.Collections.Generic;
using FlowOS.Application.DTOs;
using MediatR;

namespace FlowOS.Application.Queries;

public class GetTasksQuery : IRequest<List<TaskDto>>
{
    public Guid? TenantId { get; set; }
    public string? Assignee { get; set; } // Optional filter
}

public class GetTaskByIdQuery : IRequest<TaskDto?>
{
    public Guid TaskId { get; }
    public Guid TenantId { get; }

    public GetTaskByIdQuery(Guid taskId, Guid tenantId)
    {
        TaskId = taskId;
        TenantId = tenantId;
    }
}
