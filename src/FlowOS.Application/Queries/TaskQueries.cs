using System;
using System.Collections.Generic;
using FlowOS.Application.DTOs;
using MediatR;

namespace FlowOS.Application.Queries;

public class GetTasksQuery : IRequest<List<TaskDto>>
{
    public Guid? TenantId { get; set; }
    public string? Assignee { get; set; }
    public IReadOnlyList<string>? CallerRoles { get; set; }
}

public class GetTaskByIdQuery : IRequest<TaskDto?>
{
    public Guid TaskId { get; }
    public Guid TenantId { get; }
    public IReadOnlyList<string>? CallerRoles { get; }

    public GetTaskByIdQuery(Guid taskId, Guid tenantId, IReadOnlyList<string>? callerRoles = null)
    {
        TaskId = taskId;
        TenantId = tenantId;
        CallerRoles = callerRoles;
    }
}
