using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace FlowOS.Security.Interfaces;

public interface IJwtTokenService
{
    string GenerateToken(
        Guid userId,
        string email,
        string fullName,
        Guid tenantId,
        string tenantName,
        string role,
        IEnumerable<string>? additionalRoles = null,
        TimeSpan? lifetime = null);
    ClaimsPrincipal? ValidateToken(string token);
}
