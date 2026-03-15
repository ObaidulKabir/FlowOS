using System;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Api.Controllers;

[ApiController]
[Route("api/workflow-classes")]
[Authorize] // Requires Authenticated User (MockAuth or Real)
public class WorkflowClassesController : ControllerBase
{
    private readonly FlowOSDbContext _context;
    private readonly WorkflowClassManager _manager;
    private readonly ICurrentUser _currentUser;
    private readonly FlowOS.Domain.Validation.IWorkflowJsonLinter _linter;

    public WorkflowClassesController(
        FlowOSDbContext context,
        WorkflowClassManager manager,
        ICurrentUser currentUser,
        FlowOS.Domain.Validation.IWorkflowJsonLinter linter)
    {
        _context = context;
        _manager = manager;
        _currentUser = currentUser;
        _linter = linter;
    }

    // Helper to map Entity to DTO
    private WorkflowClassResponseDto MapToDto(WorkflowClass wc)
    {
        return new WorkflowClassResponseDto
        {
            Id = wc.Id,
            TenantId = wc.TenantId,
            Name = wc.Name,
            Version = wc.Version,
            Scope = wc.Scope,
            Status = wc.Status,
            CreatedAt = wc.CreatedAt,
            PublishedAt = wc.PublishedAt,
            PreviousVersionId = wc.PreviousVersionId,
            Definition = wc.Definition
        };
    }

    [HttpPost]
    public async Task<IActionResult> CreateDraft([FromBody] CreateWorkflowClassRequest request)
    {
        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty) return Unauthorized("TenantId is missing.");

        try 
        {
            // Create Entity
            var workflowClass = new WorkflowClass(tenantId, request.Name, request.Version, request.Definition);

            // Use Manager to enforce validation
            var validationResult = _manager.CreateDraft(workflowClass);
            if (!validationResult.IsValid)
            {
                return BadRequest(new { Errors = validationResult.Errors });
            }

            _context.WorkflowClasses.Add(workflowClass);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = workflowClass.Id }, MapToDto(workflowClass));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDraft(Guid id, [FromBody] CreateWorkflowClassRequest request)
    {
        var wc = await _context.WorkflowClasses.FindAsync(id);
        if (wc == null) return NotFound();

        if (wc.TenantId != _currentUser.TenantId) return Forbid();

        try 
        {
            wc.UpdateDraft(request.Name, request.Version, request.Definition);
            
            // Enforce validation
            var result = _manager.ValidateOnly(wc);
            if (!result.IsValid) return BadRequest(new { Errors = result.Errors });

            await _context.SaveChangesAsync();
            return Ok(MapToDto(wc));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] FlowOS.Domain.Enums.WorkflowClassScope? scope, 
        [FromQuery] FlowOS.Domain.Enums.WorkflowClassStatus? status)
    {
        var query = _context.WorkflowClasses.AsQueryable();

        var tenantId = _currentUser.TenantId;
        
        query = query.Where(wc => 
            (wc.TenantId == tenantId) || // Own
            (wc.Scope == FlowOS.Domain.Enums.WorkflowClassScope.Public) // Public Templates
        );

        if (scope.HasValue)
            query = query.Where(wc => wc.Scope == scope.Value);
        
        if (status.HasValue)
            query = query.Where(wc => wc.Status == status.Value);

        var list = await query.Select(wc => new WorkflowClassResponseDto 
        {
            Id = wc.Id,
            TenantId = wc.TenantId,
            Name = wc.Name,
            Version = wc.Version,
            Scope = wc.Scope,
            Status = wc.Status,
            CreatedAt = wc.CreatedAt,
            PublishedAt = wc.PublishedAt,
            PreviousVersionId = wc.PreviousVersionId,
            Definition = wc.Definition
        }).ToListAsync();
        
        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var wc = await _context.WorkflowClasses.FindAsync(id);
        if (wc == null) return NotFound();

        // Security Check: Tenant Isolation
        if (wc.Scope == FlowOS.Domain.Enums.WorkflowClassScope.Private && wc.TenantId != _currentUser.TenantId)
        {
            return Forbid();
        }

        return Ok(MapToDto(wc));
    }

    [HttpPost("{id}/publish")]
    public async Task<IActionResult> Publish(Guid id)
    {
        var wc = await _context.WorkflowClasses.FindAsync(id);
        if (wc == null) return NotFound();

        if (wc.TenantId != _currentUser.TenantId) return Forbid();

        var result = _manager.Publish(wc);
        if (!result.IsValid)
        {
            return BadRequest(new { Errors = result.Errors });
        }

        // Compile and Persist Runtime Definition
        try 
        {
            var definition = MapToRuntimeDefinition(wc);
            
            // Check if already exists (idempotency)
            var existing = await _context.WorkflowDefinitions
                .FirstOrDefaultAsync(d => d.Name == definition.Name && d.Version == definition.Version && d.TenantId == definition.TenantId);
                
            if (existing == null)
            {
                _context.WorkflowDefinitions.Add(definition);
            }

            // Sync Event Definitions
            if (wc.Definition?.Events != null)
            {
                foreach (var evtBp in wc.Definition.Events)
                {
                    var existingEvent = await _context.EventDefinitions
                        .FirstOrDefaultAsync(e => e.EventId == evtBp.EventId && e.TenantId == wc.TenantId);
                    
                    if (existingEvent == null)
                    {
                        var entityType = !string.IsNullOrEmpty(wc.Definition.StateMachine?.EntityType) 
                            ? wc.Definition.StateMachine.EntityType 
                            : "Workflow";

                        var newEvent = new EventDefinition(
                            evtBp.EventId,
                            wc.TenantId,
                            !string.IsNullOrEmpty(evtBp.Name) ? evtBp.Name : evtBp.EventId,
                            !string.IsNullOrEmpty(evtBp.Description) ? evtBp.Description : $"Event {evtBp.EventId}",
                            entityType,
                            evtBp.Category, // Enum
                            1, // Version
                            null, // Payload Schema
                            evtBp.IsTerminal
                        );
                        
                        newEvent.Publish(); // Auto-publish so it can be used
                        _context.EventDefinitions.Add(newEvent);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = $"Compilation failed: {ex.Message}" });
        }

        await _context.SaveChangesAsync();
        return Ok(MapToDto(wc));
    }

    private WorkflowDefinition MapToRuntimeDefinition(WorkflowClass wc)
    {
        // 1. Version Parsing
        int version = 1;
        var versionStr = wc.Version;
        if (!string.IsNullOrEmpty(versionStr))
        {
            if (versionStr.StartsWith("v", StringComparison.OrdinalIgnoreCase)) versionStr = versionStr.Substring(1);
            var majorPart = versionStr.Split(new[] { '.', '-', '+' })[0];
            if (int.TryParse(majorPart, out var v)) version = v;
        }

        // 2. Create Definition
        var def = new WorkflowDefinition(
            wc.TenantId, 
            wc.Name, 
            version, 
            wc.Definition.Workflow.StartStepId
        );

        // 3. Add Steps
        foreach (var stepBp in wc.Definition.Workflow.Steps)
        {
            if (!Enum.TryParse<WorkflowStepType>(stepBp.StepType, true, out var stepType))
            {
                // Try to handle "Action" as "Command" if needed, or throw
                if (stepBp.StepType.Equals("Action", StringComparison.OrdinalIgnoreCase)) 
                    stepType = WorkflowStepType.Command;
                else
                    throw new InvalidOperationException($"Invalid StepType '{stepBp.StepType}' in step '{stepBp.StepId}'");
            }

            var stepDef = new WorkflowStepDefinition(stepBp.StepId, stepType)
            {
                AllowedRoles = stepBp.RequiredRoles,
                NextSteps = stepBp.NextSteps,
                Conditions = stepBp.Conditions // Map Conditions
            };
            def.AddStep(stepDef);
        }
        
        // 4. Publish (Set Status)
        def.Publish();

        return def;
    }

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> SubmitForReview(Guid id)
    {
        var wc = await _context.WorkflowClasses.FindAsync(id);
        if (wc == null) return NotFound();

        if (wc.TenantId != _currentUser.TenantId) return Forbid();

        var result = _manager.SubmitForReview(wc);
        if (!result.IsValid) return BadRequest(new { Errors = result.Errors });

        await _context.SaveChangesAsync();
        return Ok(MapToDto(wc));
    }

    [HttpPost("{id}/withdraw")]
    public async Task<IActionResult> WithdrawSubmission(Guid id)
    {
        var wc = await _context.WorkflowClasses.FindAsync(id);
        if (wc == null) return NotFound();

        if (wc.TenantId != _currentUser.TenantId) return Forbid();

        var result = _manager.WithdrawSubmission(wc);
        if (!result.IsValid) return BadRequest(new { Errors = result.Errors });

        await _context.SaveChangesAsync();
        return Ok(MapToDto(wc));
    }
    
    [HttpPost("{id}/validate")]
    public async Task<IActionResult> Validate(Guid id)
    {
        var wc = await _context.WorkflowClasses.FindAsync(id);
        if (wc == null) return NotFound();
        
        // Allowed for own drafts/classes
        if (wc.TenantId != _currentUser.TenantId) return Forbid();

        var result = _manager.ValidateOnly(wc);
        // We return OK with the result, even if invalid, because "Validate" action succeeded (it ran).
        return Ok(result);
    }

    [HttpPost("lint")]
    public IActionResult Lint([FromBody] LintRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.JsonContent))
            return BadRequest("JsonContent is required.");

        var errors = _linter.Lint(request.JsonContent);
        return Ok(errors);
    }

    [HttpPost("{id}/deprecate")]
    public async Task<IActionResult> Deprecate(Guid id)
    {
        var wc = await _context.WorkflowClasses.FindAsync(id);
        if (wc == null) return NotFound();

        if (wc.TenantId != _currentUser.TenantId) return Forbid();

        var result = _manager.Deprecate(wc);
        if (!result.IsValid) return BadRequest(new { Errors = result.Errors });

        await _context.SaveChangesAsync();
        return Ok(MapToDto(wc));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var wc = await _context.WorkflowClasses.FindAsync(id);
        if (wc == null) return NotFound();

        if (wc.TenantId != _currentUser.TenantId) return Forbid();
        
        // Check for existing instances
        var hasInstances = await _context.WorkflowInstances.AnyAsync(w => w.WorkflowClassId == id);

        try 
        {
            wc.Delete(hasInstances); // Domain validation
            _context.WorkflowClasses.Remove(wc);
            await _context.SaveChangesAsync();
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message }); // Return structured error if possible, but message works
        }
    }

    [HttpPost("{id}/abandon")]
    public async Task<IActionResult> Abandon(Guid id)
    {
        var wc = await _context.WorkflowClasses.FindAsync(id);
        if (wc == null) return NotFound();

        if (wc.TenantId != _currentUser.TenantId) return Forbid();

        try 
        {
            wc.Abandon(_currentUser.TenantId); // Pass tenant ID to check Public permission
            await _context.SaveChangesAsync();
            return Ok(MapToDto(wc));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> ApproveAsPublic(Guid id)
    {
        var wc = await _context.WorkflowClasses.FindAsync(id);
        if (wc == null) return NotFound();

        var result = _manager.ApproveAsPublic(wc);
        if (!result.IsValid) return BadRequest(new { Errors = result.Errors });

        await _context.SaveChangesAsync();
        return Ok(MapToDto(wc));
    }

    [HttpPost("{id}/copy")]
    public async Task<IActionResult> CopyToTenant(Guid id, [FromBody] CopyWorkflowClassRequest request)
    {
        var wc = await _context.WorkflowClasses.FindAsync(id);
        if (wc == null) return NotFound();

        // Allow copying only if it's Public
        if (wc.Scope != FlowOS.Domain.Enums.WorkflowClassScope.Public)
             return BadRequest("Only Public WorkflowClasses can be copied.");

        if (request.NewTenantId != _currentUser.TenantId)
             return Forbid("Cannot copy to a different tenant.");

        try 
        {
            var copy = wc.CreateCopyForTenant(request.NewTenantId);
            
            // If cloning my own, user might want to set a new version or name, but for now we just copy as Draft.
            // UI can let them rename it.
            
            _context.WorkflowClasses.Add(copy);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = copy.Id }, MapToDto(copy));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
    
    [HttpPost("{id}/new-version")]
    public async Task<IActionResult> CreateNewVersion(Guid id)
    {
        var wc = await _context.WorkflowClasses.FindAsync(id);
        if (wc == null) return NotFound();

        if (wc.TenantId != _currentUser.TenantId) return Forbid();
        
        // Calculate new version (Simple SemVer increment)
        string newVersionString;
        if (System.Version.TryParse(wc.Version, out var v))
        {
            // Increment Minor version: 1.0.0 -> 1.1.0
            // If Build is present, we might want to respect it, but for simplicity let's stick to Major.Minor.Build
            var next = new System.Version(v.Major, v.Minor + 1, v.Build < 0 ? 0 : v.Build);
            newVersionString = next.ToString();
        }
        else
        {
            newVersionString = wc.Version + ".1";
        }

        var newVersion = wc.CreateNewVersion(newVersionString);
        
        _context.WorkflowClasses.Add(newVersion);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = newVersion.Id }, MapToDto(newVersion));
    }
}
