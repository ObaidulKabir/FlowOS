using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using FlowOS.Application.Commands.Security;
using FlowOS.Application.Common.Interfaces;

using Microsoft.EntityFrameworkCore;
using FlowOS.Infrastructure.Persistence; // Quick fix access

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/roles")]
public class RolesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    private readonly FlowOSDbContext _context; // Direct DB access for debug/read

    public RolesController(IMediator mediator, ICurrentUser currentUser, FlowOSDbContext context)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request)
    {
        var tenantId = _currentUser.TenantId;
        var command = new CreateRoleCommand(tenantId, request.RoleName);
        var id = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetRole), new { id }, new { id });
    }

    [HttpPost("{id}/capabilities")]
    public async Task<IActionResult> AddCapability(Guid id, [FromBody] AddCapabilityRequest request)
    {
        var tenantId = _currentUser.TenantId;
        var command = new AddCapabilityToRoleCommand(tenantId, id, request.CapabilityCode);
        var success = await _mediator.Send(command);
        
        if (!success) return NotFound();
        return Ok();
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetRole(Guid id)
    {
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == id);
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
