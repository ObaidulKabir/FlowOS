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
    public async Task Health_is_public_and_get_has_allow_header()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health")).StatusCode);

        using var request = Authorized(HttpMethod.Get, "/mcp");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        var hasAllow = response.Headers.TryGetValues("Allow", out var allowedMethods)
            || response.Content.Headers.TryGetValues("Allow", out allowedMethods);
        Assert.True(hasAllow);
        Assert.Contains("POST", allowedMethods!);
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
        Assert.Equal(12, httpNames.Length);
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
