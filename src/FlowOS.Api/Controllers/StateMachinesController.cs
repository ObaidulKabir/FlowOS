using Microsoft.AspNetCore.Mvc;
using MediatR;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StateMachinesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<StateMachinesController> _logger;

    public StateMachinesController(IMediator mediator, ILogger<StateMachinesController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateTransition([FromBody] ValidateTransitionRequest request)
    {
        // Get TenantId from header
        if (!Request.Headers.TryGetValue("x-tenant-id", out var tenantHeader) || !Guid.TryParse(tenantHeader, out var tenantId))
        {
             return BadRequest("Missing or invalid x-tenant-id header.");
        }

        var query = new ValidateStateMachineTransitionQuery
        {
            TenantId = tenantId,
            EntityType = request.EntityType,
            CurrentState = request.CurrentState,
            EventType = request.EventType
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }
}
