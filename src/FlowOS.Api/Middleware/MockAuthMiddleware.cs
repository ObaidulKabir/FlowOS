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

        // Add Tenant ID claim if provided in header or query string
        if (context.Request.Headers.TryGetValue("x-tenant-id", out var tenantId) && !string.IsNullOrWhiteSpace(tenantId))
        {
            claims.Add(new Claim("tenant_id", tenantId.ToString()));
        }
        else if (context.Request.Query.TryGetValue("tenantId", out var queryTenant) && !string.IsNullOrWhiteSpace(queryTenant))
        {
            claims.Add(new Claim("tenant_id", queryTenant.ToString()));
        }

        var identity = new ClaimsIdentity(claims, "Mock");
        context.User = new ClaimsPrincipal(identity);

        await _next(context);
    }
}
