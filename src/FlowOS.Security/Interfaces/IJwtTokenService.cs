using System;
using System.Security.Claims;

namespace FlowOS.Security.Interfaces;

public interface IJwtTokenService
{
    string GenerateToken(Guid userId, string email, string fullName, Guid tenantId, string tenantName, string role, TimeSpan? lifetime = null);
    ClaimsPrincipal? ValidateToken(string token);
}
