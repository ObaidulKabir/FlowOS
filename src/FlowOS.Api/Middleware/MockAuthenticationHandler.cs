using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using FlowOS.Core.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(Context.User, Scheme.Name)));
        }

        var env = Context.RequestServices.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
        var config = Context.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
        if (!TenantIdentityRules.AllowMockAuth(env?.EnvironmentName, config?[TenantIdentityRules.AllowMockAuthKey]))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string role = "Admin";
        if (Context.Request.Headers.TryGetValue("X-Mock-Role", out var r) && !string.IsNullOrWhiteSpace(r))
        {
            role = r.ToString();
        }

        var userId = Context.Request.Headers.TryGetValue("X-Mock-UserId", out var uid) && !string.IsNullOrWhiteSpace(uid)
            ? uid.ToString()
            : "mock-user";

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, "Mock User"),
            new Claim(ClaimTypes.Role, role)
        };

        if (Context.Request.Headers.TryGetValue("x-tenant-id", out var tenantId) &&
            TenantIdentityRules.TryParseTenant(tenantId.ToString(), out var headerTenant))
        {
            claims.Add(new Claim("tenant_id", headerTenant.ToString()));
        }
        else if (Context.Request.Query.TryGetValue("tenantId", out var queryTenant) &&
                 TenantIdentityRules.TryParseTenant(queryTenant.ToString(), out var qTenant))
        {
            claims.Add(new Claim("tenant_id", qTenant.ToString()));
        }

        var identity = new ClaimsIdentity(claims, "Mock");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Mock");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
