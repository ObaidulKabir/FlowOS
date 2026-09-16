using FlowOS.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace FlowOS.UnitTests.Infrastructure;

public class FlowOsPublicUrlsTests
{
    [Fact]
    public void Public_origin_wins_over_domain_and_request_host()
    {
        var config = NewConfig(new Dictionary<string, string?>
        {
            ["FLOWOS_PUBLIC_ORIGIN"] = "https://flowosbd.com/",
            ["FLOWOS_DOMAIN"] = "flowos.prospectbdltd.com"
        });

        var origin = FlowOsPublicUrls.ResolveOrigin(
            config,
            requestScheme: "http",
            requestHost: "localhost:8080");

        Assert.Equal("https://flowosbd.com", origin);
        Assert.Equal("https://flowosbd.com/mcp/", FlowOsPublicUrls.McpEndpoint(origin));
        Assert.Equal("/mcp/", FlowOsPublicUrls.McpPath(origin));
    }

    [Fact]
    public void Domain_is_used_when_public_origin_is_absent()
    {
        var config = NewConfig(new Dictionary<string, string?>
        {
            ["FLOWOS_DOMAIN"] = "flowos.prospectbdltd.com"
        });

        var origin = FlowOsPublicUrls.ResolveOrigin(config, requestScheme: "http", requestHost: "internal:8080");

        Assert.Equal("https://flowos.prospectbdltd.com", origin);
        Assert.Equal("https://flowos.prospectbdltd.com/mcp", FlowOsPublicUrls.McpEndpoint(origin));
    }

    [Fact]
    public void Request_and_forwarded_headers_describe_the_current_host()
    {
        var config = NewConfig(new Dictionary<string, string?>());

        var origin = FlowOsPublicUrls.ResolveOrigin(
            config,
            requestScheme: "http",
            requestHost: "flowos-mcp:8080",
            forwardedProto: "https, http",
            forwardedHost: "flowos.prospectbdltd.com");

        Assert.Equal("https://flowos.prospectbdltd.com", origin);
        Assert.Equal("https://flowos.prospectbdltd.com/mcp", FlowOsPublicUrls.McpEndpoint(origin));
    }

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/")]
    [InlineData("/MCP")]
    public void Mcp_paths_include_optional_trailing_slash(string path)
    {
        Assert.True(FlowOsPublicUrls.IsMcpPath(path));
    }

    [Fact]
    public void Other_paths_are_not_mcp()
    {
        Assert.False(FlowOsPublicUrls.IsMcpPath("/health"));
        Assert.False(FlowOsPublicUrls.IsMcpPath("/mcp/tools"));
    }

    [Theory]
    [InlineData("https://flowosbd.com", "/mcp/")]
    [InlineData("https://app.flowosbd.com/mcp", "/mcp/")]
    [InlineData("https://flowos.prospectbdltd.com", "/mcp")]
    [InlineData("http://localhost:8080", "/mcp")]
    public void Json_rpc_path_keeps_production_trailing_slash(string origin, string expectedPath)
    {
        Assert.Equal(expectedPath, FlowOsPublicUrls.McpPath(origin));
        Assert.EndsWith(expectedPath, FlowOsPublicUrls.McpEndpoint(origin));
    }

    private static IConfiguration NewConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
