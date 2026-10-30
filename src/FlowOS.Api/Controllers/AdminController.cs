using FlowOS.Api.Models;
using FlowOS.Application.Billing;
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
    private readonly ITenantApiKeyRenewalService _keyRenewal;

    public AdminController(IMediator mediator, ICurrentUser currentUser, FlowOSDbContext db, ITenantApiKeyRenewalService keyRenewal)
    {
        _keyRenewal = keyRenewal;
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

    // ── Plan management ────────────────────────────────────────────────

    [HttpGet("tenants/plan-requests")]
    public async Task<IActionResult> ListPlanRequests()
    {
        var tenants = await _db.Tenants
            .AsNoTracking()
            .ToListAsync();

        var items = tenants
            .Select(t => new { tenant = t, req = t.GetPlanRequest() })
            .Where(x => x.req != null)
            .Select(x => new TenantPlanRequestItemDto
            {
                TenantId = x.tenant.TenantId,
                TenantName = x.tenant.Name,
                CurrentPlan = x.tenant.Plan.ToString(),
                CurrentBillingStatus = x.tenant.BillingStatus.ToString(),
                CanRunRuntime = x.tenant.CanRunRuntime,
                PlanRequest = x.req!
            })
            .ToList();

        return Ok(items);
    }

    [HttpPost("tenants/{id:guid}/plan")]
    public async Task<IActionResult> SetTenantPlan(Guid id, [FromBody] SetTenantPlanRequest request)
    {
        if (!CommercialPlans.TryMap(request.Plan, out var plan, out _))
        {
            return BadRequest("Plan must be Starter, Builder, Team, Growth, Scale, Managed, or Enterprise.");
        }

        if (!Enum.TryParse<TenantBillingStatus>(request.BillingStatus, ignoreCase: true, out var billingStatus))
        {
            return BadRequest("BillingStatus must be Unpaid, Active, PastDue, or Canceled.");
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.TenantId == id);
        if (tenant == null)
            return NotFound();

        tenant.AssignPlan(plan, billingStatus);

        // Auto-approve any pending plan request
        var existingReq = tenant.GetPlanRequest();
        if (existingReq != null && existingReq.Status == "Pending")
        {
            existingReq.Status = "Approved";
            existingReq.ReviewedAt = DateTime.UtcNow;
            existingReq.ReviewedBy = _currentUser.Id ?? "admin";
            existingReq.AdminNotes = request.AdminNotes?.Trim();
            tenant.SetPlanRequest(existingReq);
        }

        await _db.SaveChangesAsync();

        // An active paid plan should unblock the tenant completely, including keys that lapsed
        // while the plan was unpaid.
        var keysExtended = tenant.CanRunRuntime
            ? await _keyRenewal.ExtendExpiringKeysAsync(tenant.TenantId)
            : 0;

        return Ok(new
        {
            keysExtended,
            tenantId = tenant.TenantId,
            name = tenant.Name,
            plan = tenant.Plan.ToString(),
            billingStatus = tenant.BillingStatus.ToString(),
            canRunRuntime = tenant.CanRunRuntime
        });
    }

    [HttpPost("tenants/{id:guid}/plan-request/reject")]
    public async Task<IActionResult> RejectPlanRequest(Guid id, [FromBody] RejectPlanRequest request)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.TenantId == id);
        if (tenant == null)
            return NotFound();

        var existingReq = tenant.GetPlanRequest();
        if (existingReq == null)
            return NotFound("No plan request found for this tenant.");

        if (existingReq.Status != "Pending")
            return BadRequest($"Plan request is already '{existingReq.Status}' and cannot be rejected again.");

        existingReq.Status = "Rejected";
        existingReq.ReviewedAt = DateTime.UtcNow;
        existingReq.ReviewedBy = _currentUser.Id ?? "admin";
        existingReq.AdminNotes = request.Reason?.Trim();
        tenant.SetPlanRequest(existingReq);

        await _db.SaveChangesAsync();

        return Ok(new { message = "Plan request rejected.", tenantId = id });
    }
}

public sealed class SetTenantPlanRequest
{
    public string Plan { get; set; } = "Managed";
    public string BillingStatus { get; set; } = "Active";
    public string? AdminNotes { get; set; }
}
