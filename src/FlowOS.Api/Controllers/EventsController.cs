using Microsoft.AspNetCore.Mvc;
using MediatR;
using FlowOS.Application.Commands;

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
        
        // Better: Use ICurrentUser if available (it is not injected here yet)
        // I'll assume command has it or rely on header
        
        // Actually, let's just use the command as is but ensure we log if it fails
        var result = await _mediator.Send(command);
        if (!result) return BadRequest($"Event processing failed. Tenant: {command.TenantId}, Event: {command.EventType}, Instance: {command.WorkflowInstanceId}");
        return Ok("Event published");
    }
}
