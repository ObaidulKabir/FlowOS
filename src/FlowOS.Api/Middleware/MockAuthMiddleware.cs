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
        if (context.Request.Headers.TryGetValue("X-Mock-Role", out var role))
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, "mock-user"),
                new Claim(ClaimTypes.Name, "Mock User"),
                new Claim(ClaimTypes.Role, role.ToString())
            };

            // Add Tenant ID claim if provided in header
            if (context.Request.Headers.TryGetValue("x-tenant-id", out var tenantId))
            {
                claims.Add(new Claim("tenant_id", tenantId.ToString()));
            }

            var identity = new ClaimsIdentity(claims, "Mock");
            context.User = new ClaimsPrincipal(identity);
        }

        await _next(context);
    }
}
