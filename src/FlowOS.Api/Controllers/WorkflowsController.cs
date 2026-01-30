using Microsoft.AspNetCore.Mvc;
using MediatR;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Queries;
using FlowOS.Workflows.Enums;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorkflowsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<WorkflowsController> _logger;
    private readonly ICurrentUser _currentUser; // Added to get TenantId

    public WorkflowsController(IMediator mediator, ILogger<WorkflowsController> logger, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _logger = logger;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetWorkflows([FromQuery] WorkflowInstanceStatus? status = WorkflowInstanceStatus.Running)
    {
        var tenantId = _currentUser.TenantId;
        var query = new GetWorkflowsQuery 
        { 
            TenantId = tenantId,
            Status = status 
        };
        var result = await _mediator.Send(query);
        return Ok(result);
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
