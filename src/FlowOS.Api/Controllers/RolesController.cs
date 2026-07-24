using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using FlowOS.Application.Commands.Security;
using FlowOS.Core.Interfaces;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize(Roles = "Admin")]
public class RolesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public RolesController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request)
    {
        var command = new CreateRoleCommand(_currentUser.TenantId, request.RoleName);
        var id = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetRole), new { id }, new { id });
    }

    [HttpPost("{id}/capabilities")]
    public async Task<IActionResult> AddCapability(Guid id, [FromBody] AddCapabilityRequest request)
    {
        var command = new AddCapabilityToRoleCommand(_currentUser.TenantId, id, request.CapabilityCode);
        var success = await _mediator.Send(command);
        
        if (!success) return NotFound();
        return Ok();
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetRole(Guid id)
    {
        var role = await _mediator.Send(new GetRoleByIdQuery(_currentUser.TenantId, id));
        if (role == null) return NotFound();
        return Ok(role);
    }
}

public class CreateRoleRequest
{
    public string RoleName { get; set; } = string.Empty;
}

public class AddCapabilityRequest
{
    public string CapabilityCode { get; set; } = string.Empty;
}
