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
        var result = await _mediator.Send(command);
        if (!result) return BadRequest("Event processing failed or workflow not found.");
        return Ok("Event published");
    }
}
