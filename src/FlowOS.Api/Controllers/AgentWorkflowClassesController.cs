using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Orchestration;
using FlowOS.Application.DTOs.Orchestration;
using FlowOS.Application.Handlers.Orchestration;
using FlowOS.Core.Interfaces;
using FlowOS.Core.Security;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.Api.Controllers;

/// <summary>
/// ADR-006 Phase 4A: HTTP surface for declarative AgentWorkflowClass blueprints.
///
/// Mirrors <see cref="WorkflowClassesController"/>: the tenant comes from
/// <see cref="ICurrentUser"/> rather than the route, and a platform administrator may
/// target another tenant with the <c>tenantId</c> query parameter.
///
/// Phase 4A is blueprint CRUD, lifecycle and validation only. Dispatch endpoints
/// (<c>/dispatch</c>, <c>/executions</c>) arrive with the Phase 4B engine.
/// </summary>
[ApiController]
[Route("api/agent-workflow-classes")]
[Authorize]
public class AgentWorkflowClassesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public AgentWorkflowClassesController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a Draft plan. Pass <c>previousVersionId</c> to start a new version of an
    /// existing published plan — there is no separate new-version endpoint, because a
    /// version is just a Draft that names its predecessor.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateDraft([FromBody] CreateAgentWorkflowClassRequest request)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");

        try
        {
            var result = await _mediator.Send(new CreateAgentWorkflowClassCommand(
                _currentUser.TenantId,
                request.Name,
                request.Version,
                request.Definition,
                request.PreviousVersionId,
                request.ChangeLog));

            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (AgentWorkflowClassValidationException ex) { return BadRequest(new { Errors = ex.ValidationResult.Errors }); }
        catch (ArgumentException ex) { return BadRequest(new { Error = ex.Message }); }
    }

    /// <summary>Update a Draft. Rejected once the plan is Published.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDraft(Guid id, [FromBody] CreateAgentWorkflowClassRequest request)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");

        try
        {
            var result = await _mediator.Send(new UpdateAgentWorkflowClassDraftCommand(
                _currentUser.TenantId, id, request.Name, request.Version, request.Definition, request.ChangeLog));

            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (AgentWorkflowClassValidationException ex) { return BadRequest(new { Errors = ex.ValidationResult.Errors }); }
        catch (InvalidOperationException ex) { return BadRequest(new { Error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { Error = ex.Message }); }
    }

    /// <summary>
    /// Publish a Draft, locking the blueprint. The blueprint is validated again here —
    /// this is the last gate before it becomes immutable.
    /// </summary>
    [HttpPost("{id}/publish")]
    public async Task<IActionResult> Publish(Guid id)
        => await Mutate(new PublishAgentWorkflowClassCommand(_currentUser.TenantId, id));

    /// <summary>Deprecate a Published plan. Existing executions continue; no new ones start.</summary>
    [HttpPost("{id}/deprecate")]
    public async Task<IActionResult> Deprecate(Guid id, [FromBody] DeprecateAgentWorkflowClassRequest? request = null)
        => await Mutate(new DeprecateAgentWorkflowClassCommand(
            _currentUser.TenantId, id, request?.Reason, request?.MigrationTargetId));

    /// <summary>Abandon a Published or Deprecated plan. Idempotent; a Draft must be deleted instead.</summary>
    [HttpPost("{id}/abandon")]
    public async Task<IActionResult> Abandon(Guid id)
        => await Mutate(new AbandonAgentWorkflowClassCommand(_currentUser.TenantId, id));

    /// <summary>
    /// Hard-delete a Draft. Guarded by GOV-DEL-001 (must be Draft) and GOV-DEL-002
    /// (no executions); both surface as 400 with the guard code in the message.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");

        try
        {
            await _mediator.Send(new DeleteAgentWorkflowClassCommand(_currentUser.TenantId, id));
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { Error = ex.Message }); }
    }

    /// <summary>
    /// Validate a blueprint without storing it, so an author can check a draft before
    /// publishing and see every problem at once. Returns the ValidationResult, including
    /// when it is invalid — a failed blueprint is a 200 with errors, not a 400.
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] ValidateAgentWorkflowBlueprintRequest request)
    {
        if (request?.Definition is null) return BadRequest(new { Error = "Definition is required." });

        var result = await _mediator.Send(new ValidateAgentWorkflowBlueprintCommand(request.Definition));
        return Ok(result);
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>List the tenant's plans, newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] WorkflowClassStatus? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] Guid? tenantId = null)
    {
        if (skip < 0) return BadRequest(new { Error = "skip cannot be negative." });
        if (take is < 1 or > 200) return BadRequest(new { Error = "take must be between 1 and 200." });

        var effectiveTenant = ResolveEffectiveTenant(tenantId);
        var list = await _mediator.Send(new ListAgentWorkflowClassesQuery(effectiveTenant, status, skip, take));
        return Ok(list);
    }

    /// <summary>Fetch one plan, including its full blueprint.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid? tenantId = null)
    {
        var effectiveTenant = ResolveEffectiveTenant(tenantId);
        var result = await _mediator.Send(new GetAgentWorkflowClassQuery(effectiveTenant, id));

        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Walk the version lineage from this plan backwards through
    /// <c>previousVersionId</c>, newest first.
    /// </summary>
    [HttpGet("{id}/lineage")]
    public async Task<IActionResult> GetLineage(
        Guid id,
        [FromQuery] int maxDepth = 10,
        [FromQuery] Guid? tenantId = null)
    {
        if (maxDepth is < 1 or > 100) return BadRequest(new { Error = "maxDepth must be between 1 and 100." });

        var effectiveTenant = ResolveEffectiveTenant(tenantId);
        var chain = await _mediator.Send(new GetAgentWorkflowClassLineageQuery(effectiveTenant, id, maxDepth));
        return Ok(chain);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsPlatformAdmin() =>
        TenantIdentityRules.IsPlatformAdministrator(_currentUser.TenantId, _currentUser.Roles);

    /// <summary>
    /// Only a platform administrator may read another tenant's plans; everyone else is
    /// pinned to their own tenant whatever the query parameter says.
    /// </summary>
    private Guid ResolveEffectiveTenant(Guid? targetTenantId)
    {
        if (!IsPlatformAdmin())
            return _currentUser.TenantId;

        if (targetTenantId.HasValue && targetTenantId.Value != Guid.Empty)
            return targetTenantId.Value;

        return _currentUser.TenantId;
    }

    private async Task<IActionResult> Mutate(IRequest<AgentWorkflowClassResponseDto> command)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");

        try
        {
            var result = await _mediator.Send(command);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (AgentWorkflowClassValidationException ex) { return BadRequest(new { Errors = ex.ValidationResult.Errors }); }
        catch (InvalidOperationException ex) { return BadRequest(new { Error = ex.Message }); }
    }
}

// ── Request bodies ────────────────────────────────────────────────────────────

/// <summary>Body for create and draft update. <c>PreviousVersionId</c> is ignored on update.</summary>
public record CreateAgentWorkflowClassRequest
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public AgentWorkflowBlueprint Definition { get; init; } = new();
    public Guid? PreviousVersionId { get; init; }
    public string? ChangeLog { get; init; }
}

public record DeprecateAgentWorkflowClassRequest
{
    public string? Reason { get; init; }
    public Guid? MigrationTargetId { get; init; }
}

public record ValidateAgentWorkflowBlueprintRequest
{
    public AgentWorkflowBlueprint? Definition { get; init; }
}
