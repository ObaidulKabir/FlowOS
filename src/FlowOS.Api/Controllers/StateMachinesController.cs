using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using FlowOS.Core.Interfaces;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StateMachinesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<StateMachinesController> _logger;

    public StateMachinesController(
        IMediator mediator,
        ICurrentUser currentUser,
        ILogger<StateMachinesController> logger)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateTransition([FromBody] ValidateTransitionRequest request)
    {
        if (_currentUser.TenantId == Guid.Empty)
            return BadRequest("Missing or invalid tenant context.");

        var query = new ValidateStateMachineTransitionQuery
        {
            TenantId = _currentUser.TenantId,
            EntityType = request.EntityType,
            CurrentState = request.CurrentState,
            EventType = request.EventType
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }
}
