using System;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.Queries;
using FlowOS.Core.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/workflows")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly IMediator _mediator;
    private readonly ILogger<WorkflowsController> _logger;

    public WorkflowsController(ICurrentUser currentUser, IMediator mediator, ILogger<WorkflowsController> logger)
    {
        _currentUser = currentUser;
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartWorkflowCommand command)
    {
        if (_currentUser.TenantId != Guid.Empty && command.TenantId != _currentUser.TenantId)
        {
             command = command with { TenantId = _currentUser.TenantId };
        }

        var result = await _mediator.Send(command);
        return Ok(new { WorkflowInstanceId = result });
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] FlowOS.Workflows.Enums.WorkflowInstanceStatus? status)
    {
        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty) return Unauthorized();

        var query = new GetWorkflowsQuery 
        { 
            TenantId = tenantId,
            Status = status
        };
        
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var tenantId = _currentUser.TenantId;

        var query = new GetWorkflowByIdQuery 
        { 
            Id = id,
            TenantId = tenantId 
        };
        
        var result = await _mediator.Send(query);
        if (result == null) 
        {
             _logger.LogWarning("GetById Workflow {WorkflowId} not found for Tenant {TenantId}", id, tenantId);
             return NotFound();
        }
        return Ok(result);
    }

    [HttpGet("{id}/audit")]
    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetAuditHistory(Guid id)
    {
        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty) return Unauthorized();

        var query = new FlowOS.Application.Queries.Admin.GetAdminWorkflowDetailQuery(id, tenantId);
        var result = await _mediator.Send(query);
        if (result == null)
        {
            _logger.LogWarning("GetAuditHistory Workflow {WorkflowId} not found for Tenant {TenantId}", id, tenantId);
            return NotFound();
        }
        return Ok(result);
    }
}
