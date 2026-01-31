using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using FlowOS.Application.Commands;
// using FlowOS.Application.Common.Interfaces; // Removed if not needed, or keep if other interfaces are used
using FlowOS.Core.Interfaces; // Changed namespace
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<EventsController> _logger;

    public EventsController(IMediator mediator, ILogger<EventsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("publish")]
    public async Task<IActionResult> PublishEvent([FromBody] PublishEventCommand command)
    {
        // Add current user logic to ensure tenant security
        // Use HttpContext to get tenant if available
        var tenantId = HttpContext.Request.Headers.TryGetValue("x-tenant-id", out var headerVal) 
             && Guid.TryParse(headerVal, out var tid) ? tid : command.TenantId;
        
        // Ensure command has the correct TenantId
        var commandWithTenant = command with { TenantId = tenantId };
        
        var result = await _mediator.Send(commandWithTenant);
        if (!result) return BadRequest($"Event processing failed. Tenant: {tenantId}, Event: {command.EventType}, Instance: {command.WorkflowInstanceId}");
        return Ok("Event published");
    }
}
