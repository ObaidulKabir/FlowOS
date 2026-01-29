using Microsoft.AspNetCore.Mvc;
using MediatR;
using FlowOS.Application.Commands;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorkflowsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<WorkflowsController> _logger;

    public WorkflowsController(IMediator mediator, ILogger<WorkflowsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartWorkflow([FromBody] StartWorkflowCommand command)
    {
        var id = await _mediator.Send(command);
        return Ok(new { WorkflowInstanceId = id });
    }

    [HttpGet("{id}")]
    public IActionResult GetWorkflow(Guid id)
    {
        return Ok(new { Id = id, Status = "Running" }); // Placeholder query for now
    }
}
