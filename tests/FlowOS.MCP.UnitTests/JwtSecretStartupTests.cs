using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace FlowOS.MCP.UnitTests;

/// <summary>
/// The MCP server validates tenant login tokens, so it must not start in Production with a missing or
/// published JWT signing secret (the repository is public).
/// </summary>
public class JwtSecretStartupTests
{
    private static void Build(string? jwtSecret)
    {
        var settings = new Dictionary<string, string?>
        {
            ["UseInMemoryDatabase"] = "true",
            ["MCP_API_KEY"] = "mcp-startup-test-key-0f3a9c7e5b1d2468ace0",
            ["FLOWOS_BILLING_ENFORCE"] = "false"
        };
        if (jwtSecret != null) settings["FlowOS:Auth:JwtSecret"] = jwtSecret;

        var app = FlowOS.MCP.Program.BuildHttpApp([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Environment.EnvironmentName = "Production";
            builder.Configuration.AddInMemoryCollection(settings);
        });
        _ = app.DisposeAsync();
    }

    [Fact]
    public void Missing_secret_stops_the_MCP_server_in_Production()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(null));
        Assert.Contains("FlowOS__Auth__JwtSecret", ex.Message);
    }

    [Fact]
    public void Secret_that_was_in_the_repository_stops_the_MCP_server_in_Production()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build("FlowOS_Super_Secret_Key_For_Tenant_Auth_2026_Minimum_32_Chars!"));
        Assert.Contains("published", ex.Message);
    }
}
