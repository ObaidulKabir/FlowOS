using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Commands; // Add this
using FlowOS.Application.DTOs.Workflows;
using FlowOS.Core.Interfaces;
using FlowOS.Infrastructure.Persistence;
using MediatR; // Add this
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/workflows")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly FlowOSDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IMediator _mediator; // Add this

    public WorkflowsController(FlowOSDbContext context, ICurrentUser currentUser, IMediator mediator)
    {
        _context = context;
        _currentUser = currentUser;
        _mediator = mediator;
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartWorkflowCommand command)
    {
        // Ensure TenantId matches context (Security)
        if (command.TenantId != _currentUser.TenantId)
        {
             // For testing, if command has tenantId, we might want to respect it if we are Admin, 
             // but for now let's enforce consistency or override it.
             // Best practice: Override it with authenticated user's tenant.
             command = command with { TenantId = _currentUser.TenantId };
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
}
