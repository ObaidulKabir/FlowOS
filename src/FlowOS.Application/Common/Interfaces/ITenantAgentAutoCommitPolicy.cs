using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Application.Common.Interfaces;

/// <summary>CR-11: tenant-level switch for agent auto-commit.</summary>
public interface ITenantAgentAutoCommitPolicy
{
    Task<bool> IsAutoCommitEnabledAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
