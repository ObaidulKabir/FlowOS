using FlowOS.Application.Commands.Admin;
using FlowOS.Application.Queries.Admin;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    private readonly FlowOSDbContext _db;

    public AdminController(IMediator mediator, ICurrentUser currentUser, FlowOSDbContext db)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _db = db;
    }

    [HttpPost("config/publish")]
    public async Task<IActionResult> PublishConfig()
    {
        try
        {
            var result = await _mediator.Send(new PublishConfigurationCommand(_currentUser.TenantId));
            if (!result.FoundConfigRoot)
                return NotFound(result.Message);

            return Ok(result.Message);
        }
        catch (Exception ex)
        {
            return BadRequest($"Configuration publish failed: {ex.Message}");
        }
    }

    [HttpGet("workflows")]
    public async Task<IActionResult> GetWorkflows()
    {
        var result = await _mediator.Send(new GetAdminWorkflowsQuery { TenantId = _currentUser.TenantId });
        return Ok(result);
    }

    [HttpGet("workflows/{id}")]
    public async Task<IActionResult> GetWorkflowDetail(Guid id)
    {
        var result = await _mediator.Send(new GetAdminWorkflowDetailQuery(id, _currentUser.TenantId));
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("state-machines")]
    public async Task<IActionResult> GetAllStateMachines()
    {
        var result = await _mediator.Send(new GetAllAdminStateMachinesQuery());
        return Ok(result);
    }

    [HttpGet("state-machines/{entityType}")]
    public async Task<IActionResult> GetStateMachine(string entityType)
    {
        var result = await _mediator.Send(new GetAdminStateMachineQuery(entityType));
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("policies")]
    public async Task<IActionResult> GetPolicies()
    {
        var result = await _mediator.Send(new GetAdminPoliciesQuery { TenantId = _currentUser.TenantId });
        return Ok(result);
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetEvents()
    {
        var result = await _mediator.Send(new GetAdminEventsQuery { TenantId = _currentUser.TenantId });
        return Ok(result);
    }

    [HttpPost("tenants/{id:guid}/plan")]
    public async Task<IActionResult> SetTenantPlan(Guid id, [FromBody] SetTenantPlanRequest request)
    {
        if (!Enum.TryParse<TenantPlan>(request.Plan, ignoreCase: true, out var plan) ||
            plan is TenantPlan.None or TenantPlan.Trial)
        {
            return BadRequest("Plan must be Managed or Enterprise.");
        }

        if (!Enum.TryParse<TenantBillingStatus>(request.BillingStatus, ignoreCase: true, out var billingStatus))
        {
            return BadRequest("BillingStatus must be Unpaid, Active, PastDue, or Canceled.");
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.TenantId == id);
        if (tenant == null)
            return NotFound();

        tenant.AssignPlan(plan, billingStatus);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            tenantId = tenant.TenantId,
            name = tenant.Name,
            plan = tenant.Plan.ToString(),
            billingStatus = tenant.BillingStatus.ToString(),
            canRunRuntime = tenant.CanRunRuntime
        });
    }
}

public sealed class SetTenantPlanRequest
{
    public string Plan { get; set; } = "Managed";
    public string BillingStatus { get; set; } = "Active";
}
