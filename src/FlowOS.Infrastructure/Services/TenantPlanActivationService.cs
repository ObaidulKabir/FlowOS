using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Billing;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Infrastructure.Persistence;

namespace FlowOS.Infrastructure.Services;

public sealed class TenantPlanActivationService : ITenantPlanActivationService
{
    private readonly FlowOSDbContext _db;
    private readonly ITenantEntitlementService _entitlements;
    private readonly TimeProvider _time;

    public TenantPlanActivationService(
        FlowOSDbContext db,
        ITenantEntitlementService entitlements,
        TimeProvider time)
    {
        _db = db;
        _entitlements = entitlements;
        _time = time;
    }

    public async Task<TenantPlanStatus?> GetStatusAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, cancellationToken);
        if (tenant == null)
            return null;

        return new TenantPlanStatus(
            tenant.Plan.ToString(),
            tenant.BillingStatus.ToString(),
            tenant.CanRunRuntime,
            _entitlements.IsEnforced,
            tenant.GetPlanRequest());
    }

    public async Task<PlanActivationResult> SubmitAsync(
        Guid tenantId,
        PlanActivationInput input,
        bool idempotent,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, cancellationToken);
        if (tenant == null)
            return new PlanActivationResult(PlanActivationOutcome.TenantNotFound);

        if (input == null || !CommercialPlans.TryMap(input.Plan, out var storedPlan, out var displayName))
            return new PlanActivationResult(PlanActivationOutcome.InvalidPlan);

        var paymentReference = input.PaymentReference?.Trim();
        var existing = tenant.GetPlanRequest();

        if (idempotent)
        {
            if (tenant.CanRunRuntime)
                return new PlanActivationResult(PlanActivationOutcome.AlreadyActive, existing);

            if (existing != null &&
                string.Equals(existing.Status, "Pending", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.RequestedPlan, displayName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.PaymentReference ?? string.Empty, paymentReference ?? string.Empty, StringComparison.Ordinal))
            {
                return new PlanActivationResult(PlanActivationOutcome.AlreadyPending, existing);
            }
        }

        var dto = new PlanRequestDto
        {
            RequestedPlan = displayName,
            CommercialTier = storedPlan.ToString(),
            PaymentReference = paymentReference,
            BillingContactEmail = input.BillingContactEmail?.Trim(),
            Notes = input.Notes?.Trim(),
            RequestedAt = _time.GetUtcNow().UtcDateTime,
            Status = "Pending"
        };

        tenant.SetPlanRequest(dto);
        await _db.SaveChangesAsync(cancellationToken);
        return new PlanActivationResult(PlanActivationOutcome.Submitted, dto);
    }
}
