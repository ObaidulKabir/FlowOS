using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using FlowOS.Application.Commands;
using FlowOS.Core.Interfaces;
using FlowOS.API.Filters;

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
    [RequireRuntimePlan]
    public async Task<IActionResult> PublishEvent([FromBody] PublishEventCommand command)
    {
        if (_currentUser.TenantId == Guid.Empty)
            return Unauthorized("TenantId is missing.");

        if (!FlowOS.Api.Http.IdempotencyKeyHeader.TryResolve(Request, command.IdempotencyKey, out var idempotencyKey, out var keyError))
            return BadRequest(new ProblemDetails { Title = "Invalid Idempotency-Key", Detail = keyError, Status = 400 });

        var commandWithTenant = command with { TenantId = _currentUser.TenantId, IdempotencyKey = idempotencyKey };

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
