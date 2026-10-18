using FlowOS.Core.Interfaces;

namespace FlowOS.Infrastructure.Services;

/// <summary>
/// Trusted worker identity for a single claimed job, never derived from HTTP headers.
/// </summary>
public sealed class AgentWorkerCurrentUser : ICurrentUser
{
    private Guid _tenantId;

    public string? Id => "flowos-agent-worker";
    public Guid TenantId => _tenantId;
    public List<string> Roles => new() { "Admin" };
    public IReadOnlyCollection<string> Scopes => Array.Empty<string>();
    public bool IsApiKey => false;

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Claim must belong to a tenant.", nameof(tenantId));
        _tenantId = tenantId;
    }
}
