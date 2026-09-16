using FlowOS.Application.Commands;
using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.ValueObjects;
using FlowOS.API.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/context-bindings")]
[Authorize]
public class ContextBindingsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;

    public ContextBindingsController(IMediator mediator, ICurrentUser currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateContextBindingRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");
        try
        {
            var result = await _mediator.Send(new CreateWorkflowContextBindingCommand(
                _currentUser.TenantId,
                request.SourceWorkflowClassId,
                request.ContextType,
                request.Name,
                request.Definition), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpPut("{id:guid}/draft")]
    public async Task<IActionResult> UpdateDraft(
        Guid id,
        [FromBody] UpdateContextBindingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _mediator.Send(new UpdateWorkflowContextBindingCommand(
                _currentUser.TenantId,
                id,
                request.Definition,
                request.SourceWorkflowClassId), cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mediator.Send(
                new ValidateWorkflowContextBindingCommand(_currentUser.TenantId, id),
                cancellationToken));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/activate")]
    [RequireRuntimePlan]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mediator.Send(
                new ActivateWorkflowContextBindingCommand(_currentUser.TenantId, id),
                cancellationToken));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (WorkflowContextBindingValidationException ex)
        {
            return BadRequest(new { Errors = ex.ValidationResult.Errors });
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("{id:guid}/archive")]
    [RequireRuntimePlan]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _mediator.Send(
                new ArchiveWorkflowContextBindingCommand(_currentUser.TenantId, id),
                cancellationToken));
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate(
        [FromBody] WorkflowContextSimulationRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.TenantId == Guid.Empty) return Unauthorized("TenantId is missing.");
        try
        {
            return Ok(await _mediator.Send(
                new SimulateWorkflowContextBindingQuery(_currentUser.TenantId, request),
                cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { Code = "CTX-SIM-NOTFOUND", Message = "Workflow context binding or revision was not found." });
        }
        catch (WorkflowContextBindingValidationException ex)
        {
            return UnprocessableEntity(new
            {
                Code = "CTX-SIM-BINDING-INVALID",
                Message = "The selected draft binding is not valid.",
                Errors = ex.ValidationResult.Errors
            });
        }
        catch (WorkflowContextPayloadException ex)
        {
            return UnprocessableEntity(new
            {
                Code = "CTX-SIM-PAYLOAD-INVALID",
                Message = ex.Message,
                Errors = ex.Errors
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Code = "CTX-SIM-ARGUMENT", Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { Code = "CTX-SIM-STATE", Message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? sourceWorkflowClassId,
        CancellationToken cancellationToken)
        => Ok(await _mediator.Send(
            new ListWorkflowContextBindingsQuery(_currentUser.TenantId, sourceWorkflowClassId),
            cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetWorkflowContextBindingQuery(_currentUser.TenantId, id),
            cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }
}

public sealed record CreateContextBindingRequest(
    Guid SourceWorkflowClassId,
    string ContextType,
    string Name,
    WorkflowContextBindingDefinition Definition);

public sealed record UpdateContextBindingRequest(
    WorkflowContextBindingDefinition Definition,
    Guid? SourceWorkflowClassId = null);
