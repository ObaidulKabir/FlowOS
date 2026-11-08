using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.Api.Controllers;

/// <summary>
/// Applications a tenant builds on FlowOS. An application groups its FlowOS API keys; create a key for it
/// with POST /api/tenants/{tenantId}/keys and an applicationId.
/// </summary>
[ApiController]
[Route("api/applications")]
[Authorize]
public class ApplicationsController : ControllerBase
{
    private readonly ITenantApplicationService _applications;
    private readonly ICurrentUser _currentUser;

    public ApplicationsController(ITenantApplicationService applications, ICurrentUser currentUser)
    {
        _applications = applications;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");
        return Ok(await _applications.ListAsync(_currentUser.TenantId));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");
        var application = await _applications.GetAsync(_currentUser.TenantId, id);
        return application == null ? NotFound(ErrorBody(TenantApplicationErrorCodes.NotFound, "Application not found.")) : Ok(application);
    }

    [HttpPost]
    public Task<IActionResult> Create([FromBody] ApplicationRequest request) =>
        Run(async () =>
        {
            var created = await _applications.CreateAsync(_currentUser.TenantId, request.Name, request.Description);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        });

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, [FromBody] ApplicationRequest request) =>
        Run(async () => Ok(await _applications.UpdateAsync(_currentUser.TenantId, id, request.Name, request.Description)));

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id) =>
        Run(async () =>
        {
            await _applications.DeleteAsync(_currentUser.TenantId, id);
            return NoContent();
        });

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");
        try
        {
            return await action();
        }
        catch (TenantApplicationException ex)
        {
            var body = ErrorBody(ex.Code, ex.Message);
            return ex.Code switch
            {
                TenantApplicationErrorCodes.NotFound => NotFound(body),
                TenantApplicationErrorCodes.NameTaken or TenantApplicationErrorCodes.InUse => Conflict(body),
                _ => BadRequest(body)
            };
        }
    }

    private static object ErrorBody(string code, string message) => new { code, message };
}

public class ApplicationRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
