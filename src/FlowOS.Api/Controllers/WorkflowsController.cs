using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.Queries;
using FlowOS.Application.DTOs.Workflows;
using FlowOS.Core.Interfaces;
using FlowOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/workflows")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly FlowOSDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IMediator _mediator;
    private readonly ILogger<WorkflowsController> _logger;

    public WorkflowsController(FlowOSDbContext context, ICurrentUser currentUser, IMediator mediator, ILogger<WorkflowsController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartWorkflowCommand command)
    {
        // Ensure TenantId matches context (Security)
        if (_currentUser.TenantId != Guid.Empty && command.TenantId != _currentUser.TenantId)
        {
             // If we have a valid current user tenant, use it.
             command = command with { TenantId = _currentUser.TenantId };
        }
        else if (_currentUser.TenantId == Guid.Empty && command.TenantId != Guid.Empty)
        {
            // Allow command to set tenant if user context is empty (e.g. initial setup or system call, though Authorize should catch this)
        }

        try 
        {
            var result = await _mediator.Send(command);
            return Ok(new { WorkflowInstanceId = result });
        }
        catch (Exception ex)
        {
            // If Policy Deny, it might throw. Global Exception Handler or Middleware should handle it.
            // But if it's a domain exception, we might want 400.
            // If it's policy, we expect 403.
            throw; // Let middleware handle
        }
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty) return Unauthorized();

        // Join Workflows with WorkflowClasses to get the Name
        var query = from w in _context.WorkflowInstances
                    join wc in _context.WorkflowClasses on w.WorkflowClassId equals wc.Id
                    where w.TenantId == tenantId
                    select new WorkflowInstanceResponseDto
                    {
                        WorkflowId = w.Id, // w.Id is the InstanceId
                        WorkflowClassId = w.WorkflowClassId,
                        WorkflowClassName = wc.Name,
                        CorrelationId = w.CorrelationId.HasValue ? w.CorrelationId.Value.ToString() : string.Empty,
                        CurrentStep = w.CurrentStepId,
                        Status = w.Status.ToString(),
                        CreatedAt = w.CreatedAt,
                        CompletedAt = w.CompletedAt
                    };

        var list = await query.OrderByDescending(w => w.CreatedAt).ToListAsync();
        return Ok(list);
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
             _logger.LogWarning($"DEBUG: GetById Workflow {id} not found for Tenant {tenantId}");
             return NotFound();
        }
        return Ok(result);
    }
}
