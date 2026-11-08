using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Governance;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Application.Handlers.Governance;
using FlowOS.Application.Queries.Governance;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Interfaces;
using FlowOS.Core.Security;
using FlowOS.Domain.Enums;
using FlowOS.API.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static FlowOS.Core.Security.ApplicationScopeRules;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/workflow-classes")]
[Authorize]
public class WorkflowClassesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantApplicationService _applications;

    public WorkflowClassesController(IMediator mediator, ICurrentUser currentUser, ITenantApplicationService applications)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _applications = applications;
    }

    [HttpPost]
    public async Task<IActionResult> CreateDraft([FromBody] CreateWorkflowClassRequest request)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");

        var applicationId = request.ApplicationId;
        if (_currentUser.ApplicationId.HasValue)
        {
            if (applicationId.HasValue && applicationId.Value != _currentUser.ApplicationId.Value)
                return ApplicationScopeForbidden();
            applicationId = _currentUser.ApplicationId;
        }

        try
        {
            var result = await _mediator.Send(new CreateWorkflowClassCommand(
                _currentUser.TenantId, request.Name, request.Version, request.Definition, applicationId));
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (WorkflowClassValidationException ex)
        {
            return BadRequest(new { Errors = ex.ValidationResult.Errors });
        }
        catch (TenantApplicationException ex)
        {
            return NotFound(new { code = ex.Code, message = ex.Message });
        }
    }

    /// <summary>
    /// Moves a workflow, with all of its versions, to an application; a null applicationId unassigns it.
    /// Reserved for an unrestricted caller - moving a workflow between applications is an
    /// administrative action, not something an application-restricted key does on its own behalf.
    /// </summary>
    [HttpPost("{id}/application")]
    public async Task<IActionResult> AssignApplication(Guid id, [FromBody] AssignWorkflowClassApplicationRequest request)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");
        if (_currentUser.ApplicationId.HasValue) return ApplicationScopeForbidden();
        try
        {
            var moved = await _applications.AssignWorkflowClassAsync(_currentUser.TenantId, id, request.ApplicationId);
            return Ok(new { workflowClassId = id, applicationId = request.ApplicationId, versionsMoved = moved });
        }
        catch (TenantApplicationException ex)
        {
            return NotFound(new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDraft(Guid id, [FromBody] CreateWorkflowClassRequest request)
    {
        var scopeError = await EnsureApplicationAccessAsync(id, _currentUser.TenantId);
        if (scopeError != null) return scopeError;

        try
        {
            var result = await _mediator.Send(new UpdateWorkflowClassCommand(
                _currentUser.TenantId, id, request.Name, request.Version, request.Definition));
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (WorkflowClassValidationException ex) { return BadRequest(new { Errors = ex.ValidationResult.Errors }); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] WorkflowClassScope? scope,
        [FromQuery] WorkflowClassStatus? status,
        [FromQuery] Guid? tenantId,
        [FromQuery] Guid? applicationId,
        [FromQuery] bool unassigned = false)
    {
        var effectiveTenant = ResolveEffectiveTenant(tenantId);
        // An application-restricted key sees only its own application's workflow classes, whatever it asks for.
        if (_currentUser.ApplicationId.HasValue)
        {
            applicationId = _currentUser.ApplicationId;
            unassigned = false;
        }
        var list = await _mediator.Send(new ListWorkflowClassesQuery(effectiveTenant, scope, status, applicationId, unassigned));
        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid? tenantId)
    {
        try
        {
            Guid effectiveTenant;
            if (IsPlatformAdmin())
            {
                var target = ResolveEffectiveTenant(tenantId);
                effectiveTenant = (target != _currentUser.TenantId && target != Guid.Empty) ? target : Guid.Empty;
            }
            else
            {
                effectiveTenant = _currentUser.TenantId;
            }

            var result = await _mediator.Send(new GetWorkflowClassByIdQuery(effectiveTenant, id));
            if (result == null) return NotFound();
            if (!Allows(_currentUser.ApplicationId, result.ApplicationId)) return NotFound();
            return Ok(result);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("{id}/publish")]
    [RequireRuntimePlan]
    public async Task<IActionResult> Publish(Guid id)
    {
        var scopeError = await EnsureApplicationAccessAsync(id, _currentUser.TenantId);
        if (scopeError != null) return scopeError;

        try
        {
            var result = await _mediator.Send(new PublishWorkflowClassCommand(_currentUser.TenantId, id));
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (WorkflowClassValidationException ex) { return BadRequest(new { Errors = ex.ValidationResult.Errors }); }
        catch (Exception ex) { return BadRequest(new { Error = $"Compilation failed: {ex.Message}" }); }
    }

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> SubmitForReview(Guid id)
    {
        var scopeError = await EnsureApplicationAccessAsync(id, _currentUser.TenantId);
        return scopeError ?? await Mutate(new SubmitWorkflowClassCommand(_currentUser.TenantId, id));
    }

    [HttpPost("{id}/withdraw")]
    public async Task<IActionResult> WithdrawSubmission(Guid id)
    {
        var scopeError = await EnsureApplicationAccessAsync(id, _currentUser.TenantId);
        return scopeError ?? await Mutate(new WithdrawWorkflowClassCommand(_currentUser.TenantId, id));
    }

    [HttpPost("{id}/validate")]
    public async Task<IActionResult> Validate(Guid id, [FromQuery] Guid? tenantId)
    {
        try
        {
            var effectiveTenant = ResolveEffectiveTenant(tenantId);
            if (effectiveTenant == _currentUser.TenantId && IsPlatformAdmin())
            {
                var existing = await _mediator.Send(new GetWorkflowClassByIdQuery(Guid.Empty, id));
                if (existing != null) effectiveTenant = existing.TenantId;
            }
            else
            {
                var scopeError = await EnsureApplicationAccessAsync(id, effectiveTenant);
                if (scopeError != null) return scopeError;
            }

            var result = await _mediator.Send(new ValidateWorkflowClassCommand(effectiveTenant, id));
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("lint")]
    public async Task<IActionResult> Lint([FromBody] LintRequestDto request)
    {
        try
        {
            var errors = await _mediator.Send(new LintWorkflowClassCommand(request.JsonContent));
            return Ok(errors);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("copilot/generate")]
    [AllowAnonymous]
    public async Task<IActionResult> GenerateCopilotBlueprint([FromBody] GenerateBlueprintCopilotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Prompt))
        {
            return BadRequest(new { Error = "Prompt cannot be empty." });
        }

        try
        {
            var result = await _mediator.Send(new GenerateWorkflowClassCopilotCommand(
                request.Prompt, request.CurrentBlueprint, request.Mode ?? "create"));
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("{id}/deprecate")]
    public async Task<IActionResult> Deprecate(Guid id)
    {
        var scopeError = await EnsureApplicationAccessAsync(id, _currentUser.TenantId);
        return scopeError ?? await Mutate(new DeprecateWorkflowClassCommand(_currentUser.TenantId, id));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var scopeError = await EnsureApplicationAccessAsync(id, _currentUser.TenantId);
        if (scopeError != null) return scopeError;

        try
        {
            await _mediator.Send(new DeleteWorkflowClassCommand(_currentUser.TenantId, id));
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { Error = ex.Message }); }
    }

    [HttpPost("{id}/abandon")]
    public async Task<IActionResult> Abandon(Guid id)
    {
        var scopeError = await EnsureApplicationAccessAsync(id, _currentUser.TenantId);
        if (scopeError != null) return scopeError;

        try
        {
            var result = await _mediator.Send(new AbandonWorkflowClassCommand(_currentUser.TenantId, id));
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { Error = ex.Message }); }
    }

    [HttpPost("{id}/approve")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [RequireRuntimePlan]
    public async Task<IActionResult> ApproveAsPublic(Guid id)
    {
        var scopeError = await EnsureApplicationAccessAsync(id, _currentUser.TenantId);
        return scopeError ?? await Mutate(new ApproveWorkflowClassCommand(_currentUser.TenantId, id));
    }

    [HttpPost("{id}/copy")]
    public async Task<IActionResult> CopyToTenant(Guid id, [FromBody] CopyWorkflowClassRequest request)
    {
        var scopeError = await EnsureApplicationAccessAsync(id, _currentUser.TenantId);
        if (scopeError != null) return scopeError;

        try
        {
            var result = await _mediator.Send(new CopyWorkflowClassCommand(_currentUser.TenantId, id, request.NewTenantId));
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return StatusCode(403, "Cannot copy to a different tenant."); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{id}/new-version")]
    public async Task<IActionResult> CreateNewVersion(Guid id, [FromQuery] VersionBumpType bump = VersionBumpType.Minor, [FromBody] NewVersionRequest? request = null)
    {
        var scopeError = await EnsureApplicationAccessAsync(id, _currentUser.TenantId);
        if (scopeError != null) return scopeError;

        try
        {
            var result = await _mediator.Send(new CreateNewWorkflowClassVersionCommand(
                _currentUser.TenantId, id, bump, request?.ChangeLog));
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("{id}/rollback")]
    public async Task<IActionResult> Rollback(Guid id)
    {
        var scopeError = await EnsureApplicationAccessAsync(id, _currentUser.TenantId);
        if (scopeError != null) return scopeError;

        try
        {
            var result = await _mediator.Send(new RollbackWorkflowClassCommand(_currentUser.TenantId, id));
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { Error = ex.Message }); }
        catch (WorkflowClassValidationException ex) { return BadRequest(new { Errors = ex.ValidationResult.Errors }); }
    }

    [HttpGet("by-name/{name}/version-tree")]
    public async Task<IActionResult> GetVersionTree(string name, [FromQuery] Guid? tenantId)
    {
        var effectiveTenant = ResolveEffectiveTenant(tenantId);
        var list = await _mediator.Send(new GetWorkflowClassVersionTreeQuery(effectiveTenant, name));
        return Ok(list);
    }

    private bool IsPlatformAdmin() =>
        TenantIdentityRules.IsPlatformAdministrator(_currentUser.TenantId, _currentUser.Roles);

    private Guid ResolveEffectiveTenant(Guid? targetTenantId)
    {
        if (!IsPlatformAdmin())
            return _currentUser.TenantId;

        if (targetTenantId.HasValue && targetTenantId.Value != Guid.Empty)
            return targetTenantId.Value;

        return _currentUser.TenantId;
    }

    /// <summary>
    /// Applications (4/4): null when the caller may act on this workflow class; otherwise the response
    /// to return instead (404 for an unknown or, for a restricted caller, out-of-scope class - the same
    /// response either way, so a restricted key cannot tell "not found" from "not yours" apart).
    /// </summary>
    private async Task<IActionResult?> EnsureApplicationAccessAsync(Guid workflowClassId, Guid tenantId)
    {
        if (!_currentUser.ApplicationId.HasValue) return null;

        var existing = await _mediator.Send(new GetWorkflowClassByIdQuery(tenantId, workflowClassId));
        if (existing == null) return NotFound();
        if (!Allows(_currentUser.ApplicationId, existing.ApplicationId)) return NotFound();
        return null;
    }

    private IActionResult ApplicationScopeForbidden() =>
        StatusCode(403, new { code = "APP-SCOPE-001", message = "This API key is restricted to one application and cannot act outside it." });

    private async Task<IActionResult> Mutate(IRequest<WorkflowClassResponseDto> command)
    {
        try
        {
            var result = await _mediator.Send(command);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (WorkflowClassValidationException ex) { return BadRequest(new { Errors = ex.ValidationResult.Errors }); }
    }
}

public record NewVersionRequest
{
    public string? ChangeLog { get; init; }
}
