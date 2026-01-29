using System;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Queries.Admin;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/admin")]
// [Authorize(Roles = "Admin")] // TODO: Add real auth
public class AdminController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public AdminController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpGet("workflows/{id}")]
    public async Task<IActionResult> GetWorkflowDetail(Guid id)
    {
        var tenantId = _currentUser.TenantId;
        var query = new GetAdminWorkflowDetailQuery(id, tenantId);
        var result = await _mediator.Send(query);
        
        if (result == null) return NotFound();
        
        return Ok(result);
    }
}
