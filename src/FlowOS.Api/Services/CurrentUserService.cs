using System;
using System.Collections.Generic;
using System.Security.Claims;
using FlowOS.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace FlowOS.API.Services;

public class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? Id => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public Guid TenantId
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return Guid.Empty;

            // Try header
            if (context.Request.Headers.TryGetValue("x-tenant-id", out var headerValue) && 
                Guid.TryParse(headerValue, out var tenantId))
            {
                return tenantId;
            }

            // Try claim
            var claim = context.User?.FindFirst("tenant_id")?.Value;
            if (Guid.TryParse(claim, out var claimId))
            {
                return claimId;
            }

            return Guid.Empty; // Or throw
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
