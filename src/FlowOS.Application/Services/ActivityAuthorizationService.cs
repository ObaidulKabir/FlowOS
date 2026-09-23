using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Interfaces;
using FlowOS.Security.Interfaces;

namespace FlowOS.Application.Services;

public sealed class ActivityAuthorizationService : IActivityAuthorizationService
{
    private readonly ICapabilityService _capabilityService;
    private readonly ICurrentUser? _currentUser;

    public ActivityAuthorizationService(
        ICapabilityService capabilityService,
        ICurrentUser? currentUser = null)
    {
        _capabilityService = capabilityService;
        _currentUser = currentUser;
    }

    public async Task AuthorizeAsync(
        Guid tenantId,
        IEnumerable<string>? callerRoles,
        IReadOnlyCollection<string> requiredCapabilities,
        bool failClosed,
        string policyName,
        string activityDescription,
        CancellationToken cancellationToken = default)
    {
        if (await IsAuthorizedAsync(tenantId, callerRoles, requiredCapabilities, failClosed, cancellationToken))
            return;

        var required = ActivityAuthorization.NormalizeCapabilities(requiredCapabilities);
        var reason = required.Count == 0
            ? $"No required capabilities are defined for {activityDescription}."
            : $"Caller lacks a required capability for {activityDescription}. Required one of: {string.Join(", ", required)}.";
        throw new PolicyViolationException(policyName, reason);
    }

    public async Task<bool> IsAuthorizedAsync(
        Guid tenantId,
        IEnumerable<string>? callerRoles,
        IReadOnlyCollection<string> requiredCapabilities,
        bool failClosed,
        CancellationToken cancellationToken = default)
    {
        var roles = callerRoles?.Where(role => !string.IsNullOrWhiteSpace(role)).ToList() ?? new List<string>();
        if (ActivityAuthorization.IsAdmin(roles))
            return true;

        var required = ActivityAuthorization.NormalizeCapabilities(requiredCapabilities);
        if (required.Count == 0)
            return !failClosed;

        var callerCaps = _currentUser?.IsApiKey == true && _currentUser.TenantId == tenantId
            ? await _capabilityService.GetEffectiveCapabilitiesAsync(
                tenantId,
                roles,
                _currentUser.Scopes,
                isApiKey: true)
            : await _capabilityService.GetCapabilitiesAsync(tenantId, roles);
        return ActivityAuthorization.HasGrant(callerCaps, required);
    }
}
