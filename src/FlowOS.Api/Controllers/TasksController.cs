using System;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<TasksController> _logger;

    public TasksController(IMediator mediator, ICurrentUser currentUser, ILogger<TasksController> logger)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetTasks()
    {
        var tenantId = _currentUser.TenantId;
        var query = new GetTasksQuery { TenantId = tenantId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetTask(Guid id)
    {
        var query = new GetTaskByIdQuery(id);
        var result = await _mediator.Send(query);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost("{id}/complete")]
    public async Task<IActionResult> CompleteTask(Guid id)
    {
        var tenantId = _currentUser.TenantId;
        
        var command = new CompleteTaskCommand(
            tenantId, 
            id, // WorkflowInstanceId
            id, // TaskId (using WorkflowInstanceId for now)
            null // CorrelationId
        );
        
        var success = await _mediator.Send(command);
        if (!success) return BadRequest("Could not complete task or task not found.");
        
        return Ok(new { success = true });
    }
}
