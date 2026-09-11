using System.Net;
using System.Text;
using FlowOS.MCP.Server;
using FlowOS.MCP.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

public sealed class HttpIntegrationTests : IAsyncLifetime
{
    private const string ApiKey = "integration-test-secret";
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private Microsoft.AspNetCore.Builder.WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _app = FlowOS.MCP.Program.BuildHttpApp([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MCP_API_KEY"] = ApiKey,
                ["MCP_ROLE"] = "Admin",
                ["MCP_ALLOWED_ORIGINS"] = "https://allowed.example"
            });
        });
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Health_and_mcp_discovery_are_public_with_allow_headers()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health")).StatusCode);

        // GET /mcp should be publicly accessible for discovery without API keys
        var unauthResponse = await _client.GetAsync("/mcp");
        Assert.Equal(HttpStatusCode.OK, unauthResponse.StatusCode);
        var hasAllow = unauthResponse.Headers.TryGetValues("Allow", out var allowedMethods)
            || unauthResponse.Content.Headers.TryGetValues("Allow", out allowedMethods);
        Assert.True(hasAllow);
        Assert.Contains("POST", allowedMethods!);
        Assert.Contains("GET", allowedMethods!);

        var jsonContent = await unauthResponse.Content.ReadAsStringAsync();
        Assert.Contains("FlowOS MCP Server", jsonContent);
        Assert.Contains("toolsCount", jsonContent);
        Assert.Contains("supportedProtocolVersions", jsonContent);
        Assert.Contains("mutating", jsonContent);
        Assert.Contains("category", jsonContent);
        Assert.Contains("tenantScoped", jsonContent);

        // HTML discovery test
        using var htmlReq = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        htmlReq.Headers.Add("Accept", "text/html");
        var htmlResponse = await _client.SendAsync(htmlReq);
        Assert.Equal(HttpStatusCode.OK, htmlResponse.StatusCode);
        var html = await htmlResponse.Content.ReadAsStringAsync();
        Assert.Contains("FlowOS MCP Control Plane", html);
        Assert.Contains("Registered Agent Tools", html);
    }

    [Fact]
    public async Task Api_key_and_tenant_are_required()
    {
        using var noKey = JsonRequest("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(noKey)).StatusCode);

        using var noTenant = JsonRequest("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        noTenant.Headers.Add("X-MCP-API-Key", ApiKey);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(noTenant)).StatusCode);
    }

    [Fact]
    public async Task Content_accept_origin_and_protocol_are_enforced()
    {
        using var wrongContent = Authorized(HttpMethod.Post, "/mcp");
        wrongContent.Content = new StringContent("{}");
        AddAccept(wrongContent);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await _client.SendAsync(wrongContent)).StatusCode);

        using var wrongAccept = Authorized(HttpMethod.Post, "/mcp");
        wrongAccept.Content = JsonContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        wrongAccept.Headers.TryAddWithoutValidation("Accept", "application/json");
        Assert.Equal(HttpStatusCode.NotAcceptable, (await _client.SendAsync(wrongAccept)).StatusCode);

        using var wrongOrigin = JsonRequest("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        Authorize(wrongOrigin);
        wrongOrigin.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(wrongOrigin)).StatusCode);

        using var missingVersion = JsonRequest("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        Authorize(missingVersion);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(missingVersion)).StatusCode);
    }

    [Fact]
    public async Task Initialize_list_call_notification_and_stdio_have_contract_parity()
    {
        var initialize = await SendAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"tests","version":"1"},"capabilities":{}}}""",
            includeProtocol: false);
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);

        var list = await SendAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var listJson = JObject.Parse(await list.Content.ReadAsStringAsync());
        var httpTools = Assert.IsType<JArray>(listJson["result"]?["tools"]);
        var httpNames = httpTools
            .Select(tool => tool["name"]!.ToString())
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(21, httpNames.Length);
        Assert.All(httpTools, tool =>
        {
            Assert.NotNull(tool["inputSchema"]);
            var description = tool["description"]?.ToString();
            Assert.NotNull(description);
            Assert.Contains("Input example:", description);
            Assert.Contains("Returns:", description);
            Assert.Contains("Errors:", description);
        });

        var call = await SendAsync(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"explain_validation_violation","arguments":{"code":"STR-001"}}}""");
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        Assert.False(JObject.Parse(await call.Content.ReadAsStringAsync())["result"]?["isError"]?.Value<bool>());

        var notifCall = await SendAsync(
            $$"""
            {
              "jsonrpc": "2.0",
              "id": 4,
              "method": "tools/call",
              "params": {
                "name": "list_notifications",
                "arguments": {
                  "tenantId": "{{TenantId}}",
                  "userId": "{{Guid.NewGuid()}}"
                }
              }
            }
            """);
        Assert.Equal(HttpStatusCode.OK, notifCall.StatusCode);
        Assert.False(JObject.Parse(await notifCall.Content.ReadAsStringAsync())["result"]?["isError"]?.Value<bool>());

        var notification = await SendAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        Assert.Equal(HttpStatusCode.Accepted, notification.StatusCode);
        Assert.Equal(string.Empty, await notification.Content.ReadAsStringAsync());

        var notificationBatch = await SendAsync(
            """[{"jsonrpc":"2.0","method":"notifications/initialized"}]""");
        Assert.Equal(HttpStatusCode.Accepted, notificationBatch.StatusCode);

        var input = new StringReader("""{"jsonrpc":"2.0","id":4,"method":"tools/list"}""" + Environment.NewLine);
        var output = new StringWriter();
        var stdio = new McpServer(_app.Services.GetRequiredService<IMcpJsonRpcDispatcher>(), input, output);
        await stdio.RunAsync(CancellationToken.None);
        var stdioNames = JObject.Parse(output.ToString())
            ["result"]?["tools"]!
            .Select(tool => tool["name"]!.ToString())
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(httpNames, stdioNames);
    }

    [Fact]
    public async Task Discovery_exposes_inputSchema_and_complete_security_metadata_for_all_tools()
    {
        var response = await _client.GetAsync("/mcp");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = JObject.Parse(await response.Content.ReadAsStringAsync());
        var tools = Assert.IsType<JArray>(json["tools"]);
        Assert.Equal(21, tools.Count);

        foreach (var tool in tools)
        {
            var name = tool["name"]?.ToString();
            Assert.False(string.IsNullOrWhiteSpace(name));

            // Formal inputSchema check
            var schema = tool["inputSchema"];
            Assert.NotNull(schema);
            Assert.Equal("object", schema["type"]?.ToString());

            // Security metadata consistency check
            Assert.NotNull(tool["authenticationRequired"]);
            Assert.NotNull(tool["authorizationRequired"]);

            if (name is "describe_workflowclass_schema" or "explain_validation_violation")
            {
                Assert.False(tool["authenticationRequired"]!.Value<bool>());
                Assert.False(tool["authorizationRequired"]!.Value<bool>());
            }
            else
            {
                Assert.True(tool["authenticationRequired"]!.Value<bool>());
            }

            var riskLevel = tool["riskLevel"]?.ToString();
            Assert.Contains(riskLevel, new[] { "low", "medium", "high" });

            var sideEffect = tool["sideEffect"]?.ToString();
            Assert.Contains(sideEffect, new[] { "none", "reversible", "irreversible" });
        }

        // Also verify HTML discovery rendering
        using var htmlReq = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        htmlReq.Headers.Add("Accept", "text/html");
        var htmlResponse = await _client.SendAsync(htmlReq);
        var html = await htmlResponse.Content.ReadAsStringAsync();
        Assert.Contains("Argument Schema (inputSchema)", html);
        Assert.Contains("Risk:", html);
    }

    [Fact]
    public async Task Cross_tenant_header_mismatch_returns_403_forbidden()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string tenantKey = "flw_live_cross_tenant_test_key_12345";

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOS.Infrastructure.Persistence.FlowOSDbContext>();
            var apiKey = new FlowOS.Domain.Entities.TenantApiKey(tenantA, "Tenant A Test Key", tenantKey);
            db.TenantApiKeys.Add(apiKey);
            await db.SaveChangesAsync();
        }

        // Attempt to access Tenant B with Tenant A's key
        using var request = JsonRequest("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"tests","version":"1"},"capabilities":{}}}""");
        request.Headers.Add("X-MCP-API-Key", tenantKey);
        request.Headers.Add("x-tenant-id", tenantB.ToString());

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JObject.Parse(body);
        Assert.Equal(-32003, json["error"]?["code"]?.Value<int>());
        Assert.Contains("Cross-tenant access forbidden", json["error"]?["message"]?.ToString());
    }

    [Fact]
    public async Task Tenant_api_key_without_header_automatically_resolves_owning_tenant()
    {
        var tenantA = Guid.NewGuid();
        const string tenantKey = "flw_live_auto_resolve_test_key_67890";

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOS.Infrastructure.Persistence.FlowOSDbContext>();
            var apiKey = new FlowOS.Domain.Entities.TenantApiKey(tenantA, "Tenant A Auto Resolve", tenantKey);
            db.TenantApiKeys.Add(apiKey);
            await db.SaveChangesAsync();
        }

        // Omit x-tenant-id header entirely: should auto-bind to tenantA
        using var request = JsonRequest("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"tests","version":"1"},"capabilities":{}}}""");
        request.Headers.Add("X-MCP-API-Key", tenantKey);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(string body, bool includeProtocol = true)
    {
        using var request = JsonRequest(body);
        Authorize(request);
        if (includeProtocol)
            request.Headers.Add("MCP-Protocol-Version", McpJsonRpcDispatcher.SupportedProtocolVersion);
        return await _client.SendAsync(request);
    }

    private static HttpRequestMessage JsonRequest(string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent(body)
        };
        AddAccept(request);
        return request;
    }

    private static StringContent JsonContent(string body) =>
        new(body, Encoding.UTF8, "application/json");

    private static HttpRequestMessage Authorized(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        Authorize(request);
        return request;
    }

    private static void Authorize(HttpRequestMessage request)
    {
        request.Headers.Add("X-MCP-API-Key", ApiKey);
        request.Headers.Add("x-tenant-id", TenantId.ToString());
    }

    private static void AddAccept(HttpRequestMessage request) =>
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
}
