using System;
using System.ComponentModel.DataAnnotations;
using FlowOS.Application.Billing;

namespace FlowOS.Api.Models;

public class SubmitPlanRequest
{
    [Required]
    public string Plan { get; set; } = "Team";
    public string? PaymentReference { get; set; }
    public string? BillingContactEmail { get; set; }
    public string? Notes { get; set; }
}

public class RejectPlanRequest
{
    public string? Reason { get; set; }
}

public class TenantPlanRequestItemDto
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string CurrentPlan { get; set; } = string.Empty;
    public string CurrentBillingStatus { get; set; } = string.Empty;
    public bool CanRunRuntime { get; set; }
    public PlanRequestDto PlanRequest { get; set; } = new();
}
