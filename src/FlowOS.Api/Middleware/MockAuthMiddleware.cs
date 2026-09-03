using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

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
        string role = "Admin";
        if (context.Request.Headers.TryGetValue("X-Mock-Role", out var r) && !string.IsNullOrWhiteSpace(r))
        {
            role = r.ToString();
        }

        var userId = context.Request.Headers.TryGetValue("X-Mock-UserId", out var uid) && !string.IsNullOrWhiteSpace(uid)
            ? uid.ToString()
            : "mock-user";

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, "Mock User"),
            new Claim(ClaimTypes.Role, role)
        };

        string? resolvedTenantId = null;

        // Extract API Key from headers or query
        string? suppliedApiKey = null;
        if (context.Request.Headers.TryGetValue("X-API-Key", out var h1) && !string.IsNullOrWhiteSpace(h1))
            suppliedApiKey = h1.ToString();
        else if (context.Request.Headers.TryGetValue("X-MCP-API-Key", out var h2) && !string.IsNullOrWhiteSpace(h2))
            suppliedApiKey = h2.ToString();
        else if (context.Request.Headers.TryGetValue("ApiKey", out var h3) && !string.IsNullOrWhiteSpace(h3))
            suppliedApiKey = h3.ToString();
        else if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) && !string.IsNullOrWhiteSpace(authHeader))
        {
            var authStr = authHeader.ToString();
            if (authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                suppliedApiKey = authStr.Substring(7).Trim();
        }
        else if (context.Request.Query.TryGetValue("apiKey", out var qKey) && !string.IsNullOrWhiteSpace(qKey))
            suppliedApiKey = qKey.ToString();

        if (!string.IsNullOrWhiteSpace(suppliedApiKey))
        {
            // Check well-known demo keys first
            if (suppliedApiKey == "flowos_prod_secret_key_32_chars_min" ||
                suppliedApiKey == "local-development-key-change-me" ||
                suppliedApiKey == "YOUR_PRODUCTION_API_KEY")
            {
                resolvedTenantId = "22222222-2222-2222-2222-222222222222";
            }
            else
            {
                try
                {
                    var db = context.RequestServices.GetService<FlowOS.Infrastructure.Persistence.FlowOSDbContext>();
                    if (db != null)
                    {
                        var keyHash = FlowOS.Domain.Entities.TenantApiKey.HashKey(suppliedApiKey);
                        var apiKeyRecord = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                            db.TenantApiKeys,
                            k => k.KeyHash == keyHash && !k.IsRevoked && (k.ExpiresAt == null || k.ExpiresAt > DateTime.UtcNow));

                        if (apiKeyRecord != null)
                        {
                            resolvedTenantId = apiKeyRecord.TenantId.ToString();
                            claims.Add(new Claim("app_name", apiKeyRecord.ApplicationName));
                            claims.Add(new Claim("environment", apiKeyRecord.Environment));
                            foreach (var scope in apiKeyRecord.Scopes)
                            {
                                claims.Add(new Claim("scope", scope));
                            }
                            apiKeyRecord.RecordUsage();
                            await db.SaveChangesAsync();
                        }
                    }
                }
                catch
                {
                    // Ignore DB lookup errors in mock auth fallback
                }
            }
        }

        // Add Tenant ID claim if provided in header, query string, or resolved via API key
        if (context.Request.Headers.TryGetValue("x-tenant-id", out var tenantId) && !string.IsNullOrWhiteSpace(tenantId))
        {
            claims.Add(new Claim("tenant_id", tenantId.ToString()));
        }
        else if (context.Request.Query.TryGetValue("tenantId", out var queryTenant) && !string.IsNullOrWhiteSpace(queryTenant))
        {
            claims.Add(new Claim("tenant_id", queryTenant.ToString()));
        }
        else if (!string.IsNullOrWhiteSpace(resolvedTenantId))
        {
            claims.Add(new Claim("tenant_id", resolvedTenantId));
        }

        var identity = new ClaimsIdentity(claims, "Mock");
        context.User = new ClaimsPrincipal(identity);

        await _next(context);
    }
}
