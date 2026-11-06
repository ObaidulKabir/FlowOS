using System;
using System.Threading.Tasks;
using FlowOS.Core.Interfaces;
using FlowOS.Security.Interfaces;

namespace FlowOS.Application.Common.Interfaces;

/// <summary>
/// Shared "does this caller hold capability X for this tenant" check, extracted from the pattern
/// already used by <c>TenantTeamMcpTools</c>/<c>TenantIamMcpTools</c> so other MCP tools and REST
/// controllers (e.g. agent-provider governance) can reuse it instead of duplicating it.
/// </summary>
public static class TenantCapabilityGate
{
    public static async Task<bool> HasCapabilityAsync(
        ICurrentUser currentUser,
        ICapabilityService capabilityService,
        Guid tenantId,
        string requiredCapability)
    {
        var capabilities = currentUser.IsApiKey
            ? await capabilityService.GetEffectiveCapabilitiesAsync(tenantId, currentUser.Roles, currentUser.Scopes, isApiKey: true)
            : await capabilityService.GetCapabilitiesAsync(tenantId, currentUser.Roles);

        return ActivityAuthorization.HasGrant(capabilities, new[] { requiredCapability });
    }
}
