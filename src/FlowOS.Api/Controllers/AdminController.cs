using FlowOS.Application.Commands.Admin;
using FlowOS.Application.Queries.Admin;
using FlowOS.Core.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public AdminController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpPost("config/publish")]
    public async Task<IActionResult> PublishConfig()
    {
        try
        {
            var result = await _mediator.Send(new PublishConfigurationCommand(_currentUser.TenantId));
            if (!result.FoundConfigRoot)
                return NotFound(result.Message);

            return Ok(result.Message);
        }
        catch (Exception ex)
        {
            return BadRequest($"Configuration publish failed: {ex.Message}");
        }
    }

    [HttpGet("workflows")]
    public async Task<IActionResult> GetWorkflows()
    {
        var result = await _mediator.Send(new GetAdminWorkflowsQuery { TenantId = _currentUser.TenantId });
        return Ok(result);
    }

    [HttpGet("workflows/{id}")]
    public async Task<IActionResult> GetWorkflowDetail(Guid id)
    {
        var result = await _mediator.Send(new GetAdminWorkflowDetailQuery(id, _currentUser.TenantId));
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("state-machines")]
    public async Task<IActionResult> GetAllStateMachines()
    {
        var result = await _mediator.Send(new GetAllAdminStateMachinesQuery());
        return Ok(result);
    }

    [HttpGet("state-machines/{entityType}")]
    public async Task<IActionResult> GetStateMachine(string entityType)
    {
        var result = await _mediator.Send(new GetAdminStateMachineQuery(entityType));
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("policies")]
    public async Task<IActionResult> GetPolicies()
    {
        var result = await _mediator.Send(new GetAdminPoliciesQuery { TenantId = _currentUser.TenantId });
        return Ok(result);
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetEvents()
    {
        var result = await _mediator.Send(new GetAdminEventsQuery { TenantId = _currentUser.TenantId });
        return Ok(result);
    }
}
