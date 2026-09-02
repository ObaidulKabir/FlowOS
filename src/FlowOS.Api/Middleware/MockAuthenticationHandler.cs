using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowOS.Api.Middleware;

public class MockAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public MockAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.User.Identity?.IsAuthenticated == true)
        {
             return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(Context.User, "Mock")));
        }

        string role = "Admin";
        if (Context.Request.Headers.TryGetValue("X-Mock-Role", out var r) && !string.IsNullOrWhiteSpace(r))
        {
            role = r.ToString();
        }

        var userId = Context.Request.Headers.TryGetValue("X-Mock-UserId", out var uid) && !string.IsNullOrWhiteSpace(uid)
            ? uid.ToString()
            : "mock-user";

        var claims = new System.Collections.Generic.List<System.Security.Claims.Claim>
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "Mock User"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)
        };

        // Add Tenant ID claim if provided in header or query string
        if (Context.Request.Headers.TryGetValue("x-tenant-id", out var tenantId) && !string.IsNullOrWhiteSpace(tenantId))
        {
            claims.Add(new System.Security.Claims.Claim("tenant_id", tenantId.ToString()));
        }
        else if (Context.Request.Query.TryGetValue("tenantId", out var queryTenant) && !string.IsNullOrWhiteSpace(queryTenant))
        {
            claims.Add(new System.Security.Claims.Claim("tenant_id", queryTenant.ToString()));
        }

        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Mock");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Mock");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
