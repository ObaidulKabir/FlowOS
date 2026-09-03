using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using FlowOS.Application.Commands;
using FlowOS.Core.Interfaces;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EventsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<EventsController> _logger;

    public EventsController(IMediator mediator, ICurrentUser currentUser, ILogger<EventsController> logger)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpPost("publish")]
    public async Task<IActionResult> PublishEvent([FromBody] PublishEventCommand command)
    {
        if (_currentUser.TenantId == Guid.Empty)
            return Unauthorized("TenantId is missing.");

        var commandWithTenant = command with { TenantId = _currentUser.TenantId };

        try
        {
            var result = await _mediator.Send(commandWithTenant);
            if (!result)
            {
                return BadRequest(
                    $"Event processing failed. Tenant: {_currentUser.TenantId}, Event: {command.EventType}, Instance: {command.WorkflowInstanceId}");
            }

            return Ok("Event published");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventType}", command.EventType);
            throw;
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetEvents([FromQuery] Guid? workflowInstanceId, [FromQuery] int limit = 50)
    {
        if (_currentUser.TenantId == Guid.Empty)
            return Unauthorized("TenantId is missing.");

        var query = new FlowOS.Application.Queries.GetPublishedEventsQuery(_currentUser.TenantId, workflowInstanceId, limit);
        var result = await _mediator.Send(query);
        return Ok(result);
    }
}
