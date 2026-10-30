using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Application.Billing;

public enum PlanActivationOutcome
{
    /// <summary>A new pending request was stored for admin review.</summary>
    Submitted,
    /// <summary>The same plan and payment reference is already pending; nothing changed.</summary>
    AlreadyPending,
    /// <summary>The tenant already has an active paid plan; nothing changed.</summary>
    AlreadyActive,
    InvalidPlan,
    TenantNotFound
}

public sealed record PlanActivationInput(
    string? Plan,
    string? PaymentReference,
    string? BillingContactEmail,
    string? Notes);

public sealed record TenantPlanStatus(
    string Plan,
    string BillingStatus,
    bool CanRunRuntime,
    bool BillingEnforced,
    PlanRequestDto? PlanRequest);

public sealed record PlanActivationResult(PlanActivationOutcome Outcome, PlanRequestDto? PlanRequest = null);

/// <summary>
/// Self-service plan activation shared by REST and MCP. It only files a request for a platform
/// administrator to review; it never activates a plan.
/// </summary>
public interface ITenantPlanActivationService
{
    Task<TenantPlanStatus?> GetStatusAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<PlanActivationResult> SubmitAsync(
        Guid tenantId,
        PlanActivationInput input,
        bool idempotent,
        CancellationToken cancellationToken = default);
}
