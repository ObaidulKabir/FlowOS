using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/dead-letters")]
[Authorize]
public class DeadLettersController : ControllerBase
{
    private readonly IDeadLetterService _deadLetterService;
    private readonly ICurrentUser _currentUser;

    public DeadLettersController(IDeadLetterService deadLetterService, ICurrentUser currentUser)
    {
        _deadLetterService = deadLetterService;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetDeadLetters(
        [FromQuery] Guid? tenantId = null,
        [FromQuery] string? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var isAdmin = _currentUser.Roles?.Contains("Admin") == true || User.IsInRole("Admin");
        var resolvedTenant = isAdmin ? tenantId : _currentUser.TenantId;

        var items = await _deadLetterService.GetDeadLettersAsync(resolvedTenant, type, page, pageSize, ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var isAdmin = _currentUser.Roles?.Contains("Admin") == true || User.IsInRole("Admin");
        var resolvedTenant = isAdmin ? (Guid?)null : _currentUser.TenantId;

        var item = await _deadLetterService.GetDeadLetterByIdAsync(id, resolvedTenant, ct);
        if (item == null) return NotFound(new { message = $"Dead letter {id} not found." });
        return Ok(item);
    }

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct = default)
    {
        var isAdmin = _currentUser.Roles?.Contains("Admin") == true || User.IsInRole("Admin");
        var resolvedTenant = isAdmin ? (Guid?)null : _currentUser.TenantId;

        var success = await _deadLetterService.RetryDeadLetterAsync(id, resolvedTenant, ct);
        if (!success) return NotFound(new { message = $"Dead letter {id} not found." });
        return Ok(new { success = true, message = $"Dead letter {id} replayed for re-dispatch." });
    }

    [HttpPost("retry-all")]
    public async Task<IActionResult> RetryAll([FromQuery] string? type = null, CancellationToken ct = default)
    {
        var isAdmin = _currentUser.Roles?.Contains("Admin") == true || User.IsInRole("Admin");
        var resolvedTenant = isAdmin ? (Guid?)null : _currentUser.TenantId;

        var count = await _deadLetterService.RetryAllDeadLettersAsync(resolvedTenant, type, ct);
        return Ok(new { success = true, replayedCount = count });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Purge(Guid id, CancellationToken ct = default)
    {
        var isAdmin = _currentUser.Roles?.Contains("Admin") == true || User.IsInRole("Admin");
        var resolvedTenant = isAdmin ? (Guid?)null : _currentUser.TenantId;

        var success = await _deadLetterService.PurgeDeadLetterAsync(id, resolvedTenant, ct);
        if (!success) return NotFound(new { message = $"Dead letter {id} not found." });
        return Ok(new { success = true, message = $"Dead letter {id} purged." });
    }
}
