using System;
using System.Collections.Generic;
using System.Security.Claims;
using FlowOS.Core.Interfaces;
using FlowOS.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FlowOS.API.Services;

public class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHostEnvironment? _environment;
    private readonly IConfiguration? _configuration;

    public CurrentUserService(
        IHttpContextAccessor httpContextAccessor,
        IHostEnvironment? environment = null,
        IConfiguration? configuration = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _environment = environment;
        _configuration = configuration;
    }

    public string? Id => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public Guid TenantId
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return Guid.Empty;

            var claimTenant = TenantIdentityRules.CredentialTenant(context.User?.FindFirst("tenant_id")?.Value);
            if (claimTenant.HasValue)
                return claimTenant.Value;

            if (!TenantIdentityRules.AllowMockAuth(
                    _environment?.EnvironmentName,
                    _configuration?[TenantIdentityRules.AllowMockAuthKey]))
            {
                return Guid.Empty;
            }

            if (context.Request.Headers.TryGetValue("x-tenant-id", out var headerValue) &&
                TenantIdentityRules.TryParseTenant(headerValue.ToString(), out var headerTenant))
            {
                return headerTenant;
            }

            if (context.Request.Query.TryGetValue("tenantId", out var queryValue) &&
                TenantIdentityRules.TryParseTenant(queryValue.ToString(), out var queryTenant))
            {
                return queryTenant;
            }

            return Guid.Empty;
        }
    }

    public List<string> Roles
    {
        get
        {
            var roles = new List<string>();
            if (_httpContextAccessor.HttpContext?.User == null) return roles;

            foreach (var claim in _httpContextAccessor.HttpContext.User.Claims)
            {
                if (claim.Type == ClaimTypes.Role)
                {
                    roles.Add(claim.Value);
                }
            }
            return roles;
        }
    }
}
