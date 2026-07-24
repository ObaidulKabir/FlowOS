using System;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Security;
using FlowOS.Core.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/policies")]
[Authorize(Roles = "Admin")]
public class PoliciesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public PoliciesController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> CreatePolicy([FromBody] CreatePolicyRequest request)
    {
        try
        {
            var id = await _mediator.Send(new CreatePolicyCommand(
                _currentUser.TenantId, request.Name, request.ConditionJson));
            return CreatedAtAction(nameof(GetPolicy), new { id }, new { id });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPolicy(Guid id)
    {
        var policy = await _mediator.Send(new GetPolicyByIdQuery(_currentUser.TenantId, id));
        if (policy == null) return NotFound();
        return Ok(policy);
    }
}

public class CreatePolicyRequest
{
    public string Name { get; set; } = string.Empty;
    public string ConditionJson { get; set; } = "{}";
}
