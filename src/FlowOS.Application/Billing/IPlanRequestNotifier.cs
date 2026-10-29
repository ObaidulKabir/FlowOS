using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Application.Billing;

/// <summary>
/// Best-effort e-mail notices around plan activation requests. Implementations must never throw:
/// a failed notice must not fail the request or the admin decision.
/// </summary>
public interface IPlanRequestNotifier
{
    Task NotifyAdminOfRequestAsync(Guid tenantId, string tenantName, PlanRequestDto request, CancellationToken cancellationToken = default);

    Task NotifyTenantOfDecisionAsync(Guid tenantId, string tenantName, PlanRequestDto request, CancellationToken cancellationToken = default);
}
