using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FlowOS.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FlowOS.Api.Middleware;

public class MockAuthMiddleware
{
    private readonly RequestDelegate _next;

    public MockAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var env = context.RequestServices.GetRequiredService<IHostEnvironment>();
        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        var allowMock = TenantIdentityRules.AllowMockAuth(
            env.EnvironmentName,
            config[TenantIdentityRules.AllowMockAuthKey]);

        var headerTenant = ReadRequestedTenant(context);
        var suppliedApiKey = ReadExplicitApiKey(context);
        var extraClaims = new List<Claim>();
        Guid? credentialTenant = null;

        if (!string.IsNullOrWhiteSpace(suppliedApiKey))
        {
            credentialTenant = await LookupApiKeyAsync(context, suppliedApiKey, extraClaims);
            if (!credentialTenant.HasValue)
            {
                var shadowedJwt = await TryAuthenticateJwtAsync(context, headerTenant);
                if (shadowedJwt == JwtAuthAttempt.Forbidden)
                    return;
                if (shadowedJwt == JwtAuthAttempt.Authenticated)
                {
                    await _next(context);
                    return;
                }
            }
        }
        else if (TryReadBearerToken(context, out var bearerToken))
        {
            var jwtAttempt = await TryAuthenticateJwtAsync(context, headerTenant);
            if (jwtAttempt == JwtAuthAttempt.Forbidden)
                return;
            if (jwtAttempt == JwtAuthAttempt.Authenticated)
            {
                await _next(context);
                return;
            }

            suppliedApiKey = bearerToken;
            credentialTenant = await LookupApiKeyAsync(context, suppliedApiKey, extraClaims);
        }

        if (credentialTenant.HasValue)
        {
            if (!await EnsureTenantMatchAsync(context, credentialTenant, headerTenant))
                return;

            var scopes = extraClaims
                .Where(c => string.Equals(c.Type, "scope", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Value);
            var role = allowMock
                ? ReadMockRole(context)
                : TenantIdentityRules.ResolveApiKeyRole(scopes, TenantIdentityRules.IsDemoApiKey(suppliedApiKey));
            var userId = allowMock ? ReadMockUserId(context) : "api-key";
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, allowMock ? "Mock User" : "API Key"),
                new Claim(ClaimTypes.Role, role),
                new Claim("tenant_id", credentialTenant.Value.ToString())
            };
            claims.AddRange(extraClaims);
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"));
            await _next(context);
            return;
        }

        if (!allowMock)
        {
            await _next(context);
            return;
        }

        var mockClaims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, ReadMockUserId(context)),
            new Claim(ClaimTypes.Name, "Mock User"),
            new Claim(ClaimTypes.Role, ReadMockRole(context))
        };

        if (headerTenant != Guid.Empty)
            mockClaims.Add(new Claim("tenant_id", headerTenant.ToString()));

        context.User = new ClaimsPrincipal(new ClaimsIdentity(mockClaims, "Mock"));
        await _next(context);
    }

    private enum JwtAuthAttempt
    {
        None,
        Forbidden,
        Authenticated
    }

    private static string? ReadExplicitApiKey(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-API-Key", out var headerKey) && !string.IsNullOrWhiteSpace(headerKey))
            return headerKey.ToString();
        if (context.Request.Headers.TryGetValue("X-MCP-API-Key", out var mcpKey) && !string.IsNullOrWhiteSpace(mcpKey))
            return mcpKey.ToString();
        if (context.Request.Headers.TryGetValue("ApiKey", out var altKey) && !string.IsNullOrWhiteSpace(altKey))
            return altKey.ToString();
        if (context.Request.Query.TryGetValue("apiKey", out var queryKey) && !string.IsNullOrWhiteSpace(queryKey))
            return queryKey.ToString();
        return null;
    }

    private static bool TryReadBearerToken(HttpContext context, out string token)
    {
        token = string.Empty;
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) ||
            string.IsNullOrWhiteSpace(authHeader))
        {
            return false;
        }

        var authStr = authHeader.ToString();
        if (!authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        token = authStr.Substring(7).Trim();
        return !string.IsNullOrWhiteSpace(token);
    }

    private static async Task<JwtAuthAttempt> TryAuthenticateJwtAsync(HttpContext context, Guid headerTenant)
    {
        if (!TryReadBearerToken(context, out var bearerToken))
            return JwtAuthAttempt.None;

        var jwtService = context.RequestServices.GetService<FlowOS.Security.Interfaces.IJwtTokenService>();
        var principal = jwtService?.ValidateToken(bearerToken);
        if (principal == null)
            return JwtAuthAttempt.None;

        if (!await EnsureTenantMatchAsync(
                context,
                TenantIdentityRules.CredentialTenant(principal.FindFirst("tenant_id")?.Value),
                headerTenant))
        {
            return JwtAuthAttempt.Forbidden;
        }

        context.User = principal;
        return JwtAuthAttempt.Authenticated;
    }

    private static async Task<Guid?> LookupApiKeyAsync(HttpContext context, string suppliedApiKey, List<Claim> extraClaims)
    {
        if (TenantIdentityRules.IsDemoApiKey(suppliedApiKey))
        {
            extraClaims.Add(new Claim("scope", ApiKeyScopeCatalog.FullAccess));
            return TenantIdentityRules.DemoTenantId;
        }

        try
        {
            var db = context.RequestServices.GetService<FlowOS.Infrastructure.Persistence.FlowOSDbContext>();
            if (db == null)
                return null;

            var keyHash = FlowOS.Domain.Entities.TenantApiKey.HashKey(suppliedApiKey);
            var apiKeyRecord = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                db.TenantApiKeys,
                k => k.KeyHash == keyHash && !k.IsRevoked && (k.ExpiresAt == null || k.ExpiresAt > DateTime.UtcNow));

            if (apiKeyRecord == null)
                return null;

            extraClaims.Add(new Claim("app_name", apiKeyRecord.ApplicationName));
            extraClaims.Add(new Claim("environment", apiKeyRecord.Environment));
            foreach (var scope in apiKeyRecord.Scopes)
            {
                extraClaims.Add(new Claim("scope", scope));
            }

            apiKeyRecord.RecordUsage();
            await db.SaveChangesAsync();
            return apiKeyRecord.TenantId;
        }
        catch
        {
            // Ignore DB lookup errors; unauthenticated callers fail closed outside Development.
            return null;
        }
    }

    private static async Task<bool> EnsureTenantMatchAsync(HttpContext context, Guid? credentialTenant, Guid headerTenant)
    {
        if (!credentialTenant.HasValue || headerTenant == Guid.Empty || headerTenant == credentialTenant.Value)
            return true;

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Cross-tenant access forbidden.");
        return false;
    }

    private static Guid ReadRequestedTenant(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("x-tenant-id", out var header) &&
            TenantIdentityRules.TryParseTenant(header.ToString(), out var fromHeader))
        {
            return fromHeader;
        }

        if (context.Request.Query.TryGetValue("tenantId", out var query) &&
            TenantIdentityRules.TryParseTenant(query.ToString(), out var fromQuery))
        {
            return fromQuery;
        }

        return Guid.Empty;
    }

    private static string ReadMockRole(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Mock-Role", out var role) && !string.IsNullOrWhiteSpace(role))
            return role.ToString();
        return "Admin";
    }

    private static string ReadMockUserId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Mock-UserId", out var uid) && !string.IsNullOrWhiteSpace(uid))
            return uid.ToString();
        return "mock-user";
    }
}
