using FlowOS.Application.Commands.Security;
using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Interfaces;
using FlowOS.Core.Security;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using FlowOS.Security.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

public sealed class TenantIamMcpTools
{
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    private readonly ICapabilityService _capabilityService;
    private readonly McpAuthorizationErrorMapper _authorizationErrorMapper;
    private readonly ILogger<TenantIamMcpTools> _logger;

    public TenantIamMcpTools(
        IMediator mediator,
        ICurrentUser currentUser,
        ICapabilityService capabilityService,
        McpAuthorizationErrorMapper authorizationErrorMapper,
        ILogger<TenantIamMcpTools> logger)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _capabilityService = capabilityService;
        _authorizationErrorMapper = authorizationErrorMapper;
        _logger = logger;
    }

    public async Task<CallToolResult> DiagnoseCallerPermissions(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var roleCapabilities = await _capabilityService.GetCapabilitiesAsync(tenantId, _currentUser.Roles);
            var effectiveCapabilities = _currentUser.IsApiKey
                ? await _capabilityService.GetEffectiveCapabilitiesAsync(
                    tenantId,
                    _currentUser.Roles,
                    _currentUser.Scopes,
                    isApiKey: true)
                : roleCapabilities;

            var requiredCapability = args["requiredCapability"]?.ToString()?.Trim();
            bool? authorized = string.IsNullOrWhiteSpace(requiredCapability)
                ? null
                : ActivityAuthorization.HasGrant(
                    effectiveCapabilities,
                    new[] { requiredCapability });

            return McpToolResults.Success(new
            {
                tenantId,
                credentialType = _currentUser.IsApiKey ? "tenantApiKey" : "serviceOrUser",
                roles = _currentUser.Roles,
                scopes = _currentUser.Scopes,
                roleCapabilities = roleCapabilities.OrderBy(item => item).ToArray(),
                effectiveCapabilities = effectiveCapabilities.OrderBy(item => item).ToArray(),
                requiredCapability,
                authorized,
                scopeBoundary = _currentUser.IsApiKey && !ApiKeyScopeCatalog.HasFullAccess(_currentUser.Scopes),
                guidance = authorized == false
                    ? "The tenant role must contain the capability and the API key must include its mapped scope. Rotate immutable key scopes when necessary."
                    : "Effective capabilities are the intersection of tenant-role permissions and API-key scopes."
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to diagnose caller permissions: {ex.Message}");
        }
    }

    public async Task<CallToolResult> ListTenantRoles(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var denied = await RequireCapabilityAsync(tenantId, "iam.read", "list_tenant_roles");
            if (denied != null) return denied;

            var roles = await _mediator.Send(new ListRolesQuery(tenantId));
            return McpToolResults.Success(new
            {
                tenantId,
                roles = roles.Select(role => new
                {
                    role.Id,
                    role.Name,
                    capabilities = role.Permissions.OrderBy(item => item).ToArray(),
                    reserved = TenantSecurityDefaults.ReservedRoles.ContainsKey(role.Name)
                }).ToArray()
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to list tenant roles: {ex.Message}");
        }
    }

    public async Task<CallToolResult> CreateTenantRole(JObject args)
    {
        var tenantId = Guid.Empty;
        try
        {
            tenantId = McpTenantResolver.ResolveRequired(args);
            var denied = await RequireMutationAuthorityAsync(args, tenantId, "create_tenant_role");
            if (denied != null) return denied;

            var roleName = args["roleName"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(roleName))
                return McpToolResults.Fail("MCP-ARG-001", "roleName is required.");

            var roleId = await _mediator.Send(new CreateRoleCommand(tenantId, roleName));
            _logger.LogWarning(
                "Tenant IAM role {RoleName} ({RoleId}) created through MCP for tenant {TenantId} by {ActorId}.",
                roleName,
                roleId,
                tenantId,
                _currentUser.Id);

            return McpToolResults.Success(new { tenantId, roleId, roleName, created = true });
        }
        catch (PolicyViolationException ex)
        {
            return await _authorizationErrorMapper.MapAsync(ex, "create_tenant_role", tenantId);
        }
        catch (InvalidOperationException ex)
        {
            return McpToolResults.Fail("MCP-IAM-001", ex.Message);
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to create tenant role: {ex.Message}");
        }
    }

    public Task<CallToolResult> GrantRoleCapability(JObject args)
        => ChangeRoleCapability(args, grant: true);

    public Task<CallToolResult> RevokeRoleCapability(JObject args)
        => ChangeRoleCapability(args, grant: false);

    private async Task<CallToolResult> ChangeRoleCapability(JObject args, bool grant)
    {
        var tenantId = Guid.Empty;
        var activity = grant ? "grant_role_capability" : "revoke_role_capability";
        try
        {
            tenantId = McpTenantResolver.ResolveRequired(args);
            var denied = await RequireMutationAuthorityAsync(args, tenantId, activity);
            if (denied != null) return denied;

            if (!Guid.TryParse(args["roleId"]?.ToString(), out var roleId))
                return McpToolResults.Fail("MCP-ARG-002", "roleId must be a valid UUID.");

            var capabilityCode = args["capabilityCode"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(capabilityCode))
                return McpToolResults.Fail("MCP-ARG-001", "capabilityCode is required.");

            var changed = grant
                ? await _mediator.Send(new AddCapabilityToRoleCommand(tenantId, roleId, capabilityCode))
                : await _mediator.Send(new RemoveCapabilityFromRoleCommand(tenantId, roleId, capabilityCode));
            if (!changed)
                return McpToolResults.Fail("MCP-IAM-002", $"Role '{roleId}' was not found for this tenant.");

            _logger.LogWarning(
                "Tenant IAM capability {CapabilityCode} was {Operation} role {RoleId} through MCP for tenant {TenantId} by {ActorId}.",
                capabilityCode,
                grant ? "granted to" : "revoked from",
                roleId,
                tenantId,
                _currentUser.Id);

            return McpToolResults.Success(new
            {
                tenantId,
                roleId,
                capabilityCode,
                operation = grant ? "granted" : "revoked"
            });
        }
        catch (PolicyViolationException ex)
        {
            return await _authorizationErrorMapper.MapAsync(ex, activity, tenantId);
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to {activity.Replace('_', ' ')}: {ex.Message}");
        }
    }

    private async Task<CallToolResult?> RequireMutationAuthorityAsync(
        JObject args,
        Guid tenantId,
        string activity)
    {
        if (args["confirmHumanApproval"]?.Value<bool>() != true)
        {
            return McpToolResults.Fail(
                "MCP-APPROVAL-REQUIRED",
                $"{activity} changes tenant IAM and requires confirmHumanApproval: true.");
        }

        return await RequireCapabilityAsync(tenantId, "iam.manage", activity);
    }

    private async Task<CallToolResult?> RequireCapabilityAsync(
        Guid tenantId,
        string requiredCapability,
        string activity)
    {
        var capabilities = _currentUser.IsApiKey
            ? await _capabilityService.GetEffectiveCapabilitiesAsync(
                tenantId,
                _currentUser.Roles,
                _currentUser.Scopes,
                isApiKey: true)
            : await _capabilityService.GetCapabilitiesAsync(tenantId, _currentUser.Roles);

        if (ActivityAuthorization.HasGrant(capabilities, new[] { requiredCapability }))
            return null;

        return McpToolResults.Fail(
            "MCP-AUTHZ-001",
            $"Missing required capability: {requiredCapability}",
            new
            {
                activity,
                tenantId,
                requiredAnyOf = new[] { requiredCapability },
                callerRoles = _currentUser.Roles,
                callerScopes = _currentUser.Scopes,
                effectiveCapabilities = capabilities.OrderBy(item => item).ToArray()
            });
    }
}
