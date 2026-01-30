using System;
using FlowOS.Application.DTOs.Admin;
using MediatR;

namespace FlowOS.Application.Queries.Admin;

public class GetAdminWorkflowDetailQuery : IRequest<AdminWorkflowDetailDto>
{
    public Guid WorkflowInstanceId { get; set; }
    public Guid TenantId { get; set; }

    public GetAdminWorkflowDetailQuery(Guid workflowInstanceId, Guid tenantId)
    {
        WorkflowInstanceId = workflowInstanceId;
        TenantId = tenantId;
    }
}

public class GetAdminStateMachineQuery : IRequest<AdminStateMachineDto>
{
    public string EntityType { get; set; }
    
    public GetAdminStateMachineQuery(string entityType)
    {
        EntityType = entityType;
    }
}

public class GetAllAdminStateMachinesQuery : IRequest<List<AdminStateMachineDto>>
{
}

public class GetAdminPoliciesQuery : IRequest<List<AdminPolicyDto>>
{
    public Guid TenantId { get; set; }
}

public class GetAdminEventsQuery : IRequest<List<AdminEventDefinitionDto>>
{
    public Guid TenantId { get; set; }
}

public class GetAdminWorkflowsQuery : IRequest<List<AdminWorkflowSummaryDto>>
{
    public Guid TenantId { get; set; }
}
