using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Governance;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Application.Handlers.Governance;
using FlowOS.Application.Queries.Governance;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/workflow-classes")]
[Authorize]
public class WorkflowClassesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public WorkflowClassesController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> CreateDraft([FromBody] CreateWorkflowClassRequest request)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");

        try
        {
            var result = await _mediator.Send(new CreateWorkflowClassCommand(
                _currentUser.TenantId, request.Name, request.Version, request.Definition));
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (WorkflowClassValidationException ex)
        {
            return BadRequest(new { Errors = ex.ValidationResult.Errors });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDraft(Guid id, [FromBody] CreateWorkflowClassRequest request)
    {
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
        [FromQuery] WorkflowClassStatus? status)
    {
        var list = await _mediator.Send(new ListWorkflowClassesQuery(_currentUser.TenantId, scope, status));
        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var result = await _mediator.Send(new GetWorkflowClassByIdQuery(_currentUser.TenantId, id));
            if (result == null) return NotFound();
            return Ok(result);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("{id}/publish")]
    public async Task<IActionResult> Publish(Guid id)
    {
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
        => await Mutate(new SubmitWorkflowClassCommand(_currentUser.TenantId, id));

    [HttpPost("{id}/withdraw")]
    public async Task<IActionResult> WithdrawSubmission(Guid id)
        => await Mutate(new WithdrawWorkflowClassCommand(_currentUser.TenantId, id));

    [HttpPost("{id}/validate")]
    public async Task<IActionResult> Validate(Guid id)
    {
        try
        {
            var result = await _mediator.Send(new ValidateWorkflowClassCommand(_currentUser.TenantId, id));
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

    [HttpPost("{id}/deprecate")]
    public async Task<IActionResult> Deprecate(Guid id)
        => await Mutate(new DeprecateWorkflowClassCommand(_currentUser.TenantId, id));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
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
    public async Task<IActionResult> ApproveAsPublic(Guid id)
        => await Mutate(new ApproveWorkflowClassCommand(_currentUser.TenantId, id));

    [HttpPost("{id}/copy")]
    public async Task<IActionResult> CopyToTenant(Guid id, [FromBody] CopyWorkflowClassRequest request)
    {
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
    public async Task<IActionResult> CreateNewVersion(Guid id)
    {
        try
        {
            var result = await _mediator.Send(new CreateNewWorkflowClassVersionCommand(_currentUser.TenantId, id));
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

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
