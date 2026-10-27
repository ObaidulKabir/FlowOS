using System;
using System.Threading.Tasks;
using FlowOS.Application.Commands;
using FlowOS.Core.Interfaces;
using FlowOS.Application.Queries;
using FlowOS.API.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
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

    public const int MaxPageSize = 500;

    /// <summary>
    /// CR-12: the caller's inbox, highest priority first, then earliest SLA due time, then oldest.
    /// Filters: role, minPriority, dueBefore. Paging: skip and take; X-Total-Count has the filtered total.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetTasks(
        [FromQuery] string? role = null,
        [FromQuery] int? minPriority = null,
        [FromQuery] DateTimeOffset? dueBefore = null,
        [FromQuery] int skip = 0,
        [FromQuery] int? take = null)
    {
        if (skip < 0)
            return BadRequest(new ProblemDetails { Title = "Invalid skip", Detail = "skip must be 0 or more.", Status = 400 });
        if (take is < 1 or > MaxPageSize)
            return BadRequest(new ProblemDetails { Title = "Invalid take", Detail = $"take must be between 1 and {MaxPageSize}.", Status = 400 });

        var query = new GetTasksQuery
        {
            TenantId = _currentUser.TenantId,
            CallerRoles = _currentUser.Roles,
            Role = role,
            MinPriority = minPriority,
            DueBeforeUtc = dueBefore?.UtcDateTime
        };
        var result = await _mediator.Send(query);

        Response.Headers["X-Total-Count"] = result.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        IEnumerable<FlowOS.Application.DTOs.TaskDto> page = result.Skip(skip);
        if (take.HasValue) page = page.Take(take.Value);
        return Ok(page.ToList());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetTask(Guid id)
    {
        var query = new GetTaskByIdQuery(id, _currentUser.TenantId, _currentUser.Roles);
        var result = await _mediator.Send(query);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost("{id}/complete")]
    [RequireRuntimePlan]
    public async Task<IActionResult> CompleteTask(Guid id)
    {
        var tenantId = _currentUser.TenantId;

        if (!FlowOS.Api.Http.IdempotencyKeyHeader.TryResolve(Request, null, out var idempotencyKey, out var keyError))
            return BadRequest(new ProblemDetails { Title = "Invalid Idempotency-Key", Detail = keyError, Status = 400 });

        var command = new CompleteTaskCommand(
            tenantId,
            id, // WorkflowInstanceId
            id, // TaskId (using WorkflowInstanceId for now)
            null, // CorrelationId
            idempotencyKey);
        
        var success = await _mediator.Send(command);
        if (!success) 
        {
             return BadRequest("Could not complete task or task not found.");
        }
        
        return Ok();
    }
}
