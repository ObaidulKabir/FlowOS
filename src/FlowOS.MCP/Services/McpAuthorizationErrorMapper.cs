using FlowOS.Application.Common.Exceptions;
using FlowOS.Core.Interfaces;
using FlowOS.MCP.Models;
using FlowOS.Security.Interfaces;

namespace FlowOS.MCP.Services;

public sealed class McpAuthorizationErrorMapper
{
    private readonly ICurrentUser _currentUser;
    private readonly ICapabilityService _capabilityService;

    public McpAuthorizationErrorMapper(
        ICurrentUser currentUser,
        ICapabilityService capabilityService)
    {
        _currentUser = currentUser;
        _capabilityService = capabilityService;
    }

    public async Task<CallToolResult> MapAsync(
        PolicyViolationException exception,
        string activity,
        Guid tenantId)
    {
        var policyName = exception.PolicyName ?? "Policy";
        var code = ResolveCode(policyName);
        var capabilities = _currentUser.IsApiKey
            ? await _capabilityService.GetEffectiveCapabilitiesAsync(
                tenantId,
                _currentUser.Roles,
                _currentUser.Scopes,
                isApiKey: true)
            : await _capabilityService.GetCapabilitiesAsync(tenantId, _currentUser.Roles);

        return McpToolResults.Fail(
            code,
            exception.Reason,
            new
            {
                policyName,
                activity,
                tenantId,
                requiredAnyOf = ExtractRequiredCapabilities(exception.Reason),
                callerRoles = _currentUser.Roles,
                callerScopes = _currentUser.Scopes,
                effectiveCapabilities = capabilities.OrderBy(item => item).ToArray(),
                remediation = new[]
                {
                    "Call diagnose_caller_permissions to inspect the effective role and API-key scope boundary.",
                    "Use a tenant Admin credential to grant the role capability or rotate the application key with the required scope."
                }
            });
    }

    private static string ResolveCode(string policyName)
    {
        if (policyName.Contains("Business", StringComparison.OrdinalIgnoreCase) ||
            policyName.Contains("ContextTaskRole", StringComparison.OrdinalIgnoreCase))
        {
            return "MCP-AUTHZ-002";
        }

        if (!policyName.Contains("Capability", StringComparison.OrdinalIgnoreCase) &&
            !policyName.Contains("ActivityAuthorization", StringComparison.OrdinalIgnoreCase) &&
            !policyName.Contains("EventPermission", StringComparison.OrdinalIgnoreCase))
        {
            return "MCP-AUTHZ-003";
        }

        return "MCP-AUTHZ-001";
    }

    private static string[] ExtractRequiredCapabilities(string reason)
    {
        const string requiredOneOf = "Required one of:";
        var oneOfIndex = reason.IndexOf(requiredOneOf, StringComparison.OrdinalIgnoreCase);
        if (oneOfIndex >= 0)
        {
            return reason[(oneOfIndex + requiredOneOf.Length)..]
                .Trim()
                .TrimEnd('.')
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        const string missingCapability = "Missing required capability:";
        var missingIndex = reason.IndexOf(missingCapability, StringComparison.OrdinalIgnoreCase);
        if (missingIndex >= 0)
        {
            var value = reason[(missingIndex + missingCapability.Length)..].Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value };
        }

        return Array.Empty<string>();
    }
}
