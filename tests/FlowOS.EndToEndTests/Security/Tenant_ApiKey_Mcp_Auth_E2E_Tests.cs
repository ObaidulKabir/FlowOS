using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Domain.Entities;
using FlowOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Security;

/// <summary>
/// A tenant-issued API key must authenticate that tenant on the API and on MCP
/// (initialize, tools/list, tools/call), including Cursor Origin and /mcp/.
/// </summary>
public sealed class Tenant_ApiKey_Mcp_Auth_E2E_Tests : IAsyncLifetime
{
    private const string PlatformMcpKey = "e2e-mcp-platform-secret";
    private const string InitializeBody =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"cursor-e2e","version":"1"},"capabilities":{}}}""";
    private const string ToolsListBody =
        """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""";
    private const string ListPublicClassesBody =
        """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_public_workflowclasses","arguments":{}}}""";

    private readonly string _sharedDb = "FlowOS_E2E_McpTenantKey_" + Guid.NewGuid().ToString("N");
    private readonly string _isolatedDb = "FlowOS_E2E_McpIsolated_" + Guid.NewGuid().ToString("N");

    private WebApplicationFactory<Program> _apiFactory = null!;
    private WebApplication _mcp = null!;
    private WebApplication _isolatedMcp = null!;
    private HttpClient _api = null!;
    private HttpClient _mcpClient = null!;
    private HttpClient _isolatedMcpClient = null!;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public Tenant_ApiKey_Mcp_Auth_E2E_Tests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _apiFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.ConfigureTestServices(services => UseNamedInMemoryDatabase(services, _sharedDb));
        });
        _api = _apiFactory.CreateClient();

        _mcp = BuildMcp(_sharedDb, allowedOrigins: "");
        await _mcp.StartAsync();
        _mcpClient = _mcp.GetTestClient();

        _isolatedMcp = BuildMcp(_isolatedDb, allowedOrigins: "");
        await _isolatedMcp.StartAsync();
        _isolatedMcpClient = _isolatedMcp.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _api.Dispose();
        _mcpClient.Dispose();
        _isolatedMcpClient.Dispose();
        await _mcp.DisposeAsync();
        await _isolatedMcp.DisposeAsync();
        await _apiFactory.DisposeAsync();
    }

    [Fact]
    public async Task Specific_tenant_api_key_authenticates_api_and_mcp_tools()
    {
        var register = await _api.PostAsJsonAsync("/api/tenants", new
        {
            name = $"E2E MCP Tenant {Guid.NewGuid():N}",
            keyName = "Registration Key",
            applicationName = "Dashboard",
            environment = "Production"
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        using var registerJson = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        var tenantId = registerJson.RootElement.GetProperty("tenant").GetProperty("tenantId").GetGuid();
        var registrationKey = registerJson.RootElement.GetProperty("apiKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(registrationKey));
        Assert.StartsWith("flw_live_", registrationKey);

        var lookup = await _api.GetAsync($"/api/tenants/{tenantId}");
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        using var lookupJson = JsonDocument.Parse(await lookup.Content.ReadAsStringAsync());
        Assert.Equal(tenantId, lookupJson.RootElement.GetProperty("tenantId").GetGuid());

        var generate = await _api.PostAsJsonAsync($"/api/tenants/{tenantId}/keys", new
        {
            name = "Cursor MCP Key",
            applicationName = "Cursor",
            environment = "Production",
            scopes = new[] { "*" }
        });
        Assert.Equal(HttpStatusCode.OK, generate.StatusCode);

        using var generateJson = JsonDocument.Parse(await generate.Content.ReadAsStringAsync());
        var generatedKey = generateJson.RootElement.GetProperty("apiKey").GetString();
        var generatedKeyId = generateJson.RootElement.GetProperty("id").GetGuid();
        Assert.False(string.IsNullOrWhiteSpace(generatedKey));
        Assert.NotEqual(generateJson.RootElement.GetProperty("maskedKey").GetString(), generatedKey);
        Assert.Equal(tenantId, generateJson.RootElement.GetProperty("tenantId").GetGuid());

        using (var scope = _apiFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var storedKeys = await db.TenantApiKeys.AsNoTracking()
                .Where(k => k.TenantId == tenantId)
                .ToListAsync();
            Assert.Equal(2, storedKeys.Count);

            var storedGenerated = Assert.Single(storedKeys, k => k.Id == generatedKeyId);
            Assert.Equal(TenantApiKey.HashKey(generatedKey!), storedGenerated.KeyHash);
            Assert.DoesNotContain(generatedKey!, storedGenerated.KeyHash);
            Assert.DoesNotContain(generatedKey!, storedGenerated.MaskedKey);
            Assert.DoesNotContain(generatedKey!, storedGenerated.KeyPrefix);
            Assert.StartsWith("flw_live_", storedGenerated.KeyPrefix);

            var storedRegistration = storedKeys.Single(k => k.Id != generatedKeyId);
            Assert.Equal(TenantApiKey.HashKey(registrationKey!), storedRegistration.KeyHash);
            Assert.DoesNotContain(registrationKey!, storedRegistration.KeyHash);
        }

        var apiWithKey = await _api.SendAsync(AuthorizedApi(HttpMethod.Get, $"/api/tenants/{tenantId}", generatedKey!, tenantId));
        Assert.Equal(HttpStatusCode.OK, apiWithKey.StatusCode);

        var discovery = await _mcpClient.GetAsync("/mcp");
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        Assert.Contains("FlowOS MCP Server", await discovery.Content.ReadAsStringAsync());

        var initialize = await SendMcpAsync(_mcpClient, "/mcp", InitializeBody, tenantId, generatedKey!, origin: "https://www.cursor.com");
        AssertMcpSuccess(initialize.response, initialize.body);
        Assert.Equal("https://www.cursor.com", initialize.response.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var toolsList = await SendMcpAsync(_mcpClient, "/mcp", ToolsListBody, tenantId, generatedKey!, origin: "https://www.cursor.com");
        AssertMcpSuccess(toolsList.response, toolsList.body);
        using (var toolsJson = JsonDocument.Parse(toolsList.body))
        {
            var tools = toolsJson.RootElement.GetProperty("result").GetProperty("tools");
            Assert.True(tools.GetArrayLength() > 0);
            Assert.Contains(tools.EnumerateArray(), t => t.GetProperty("name").GetString() == "list_public_workflowclasses");
        }

        var toolCall = await SendMcpAsync(_mcpClient, "/mcp", ListPublicClassesBody, tenantId, generatedKey!);
        AssertMcpSuccess(toolCall.response, toolCall.body);

        var slashPath = await SendMcpAsync(_mcpClient, "/mcp/", InitializeBody, tenantId, generatedKey!);
        AssertMcpSuccess(slashPath.response, slashPath.body);

        var xApiKey = await SendMcpAsync(
            _mcpClient, "/mcp", InitializeBody, tenantId, generatedKey!, keyHeader: "X-API-Key");
        AssertMcpSuccess(xApiKey.response, xApiKey.body);

        var registrationAuth = await SendMcpAsync(_mcpClient, "/mcp", InitializeBody, tenantId, registrationKey!);
        AssertMcpSuccess(registrationAuth.response, registrationAuth.body);

        var keysAfterUse = await _api.GetAsync($"/api/tenants/{tenantId}/keys");
        Assert.Equal(HttpStatusCode.OK, keysAfterUse.StatusCode);
        using var keysJson = JsonDocument.Parse(await keysAfterUse.Content.ReadAsStringAsync());
        var usedKey = keysJson.RootElement.EnumerateArray()
            .Single(k => k.GetProperty("id").GetGuid() == generatedKeyId);
        Assert.True(usedKey.TryGetProperty("lastUsedAt", out var lastUsed) && lastUsed.ValueKind == JsonValueKind.String);

        var unknown = await SendMcpAsync(_mcpClient, "/mcp", InitializeBody, tenantId, "flw_live_unknown_key_not_in_database");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.response.StatusCode);
        Assert.Contains("Invalid or unknown tenant API key", unknown.body);

        var foreignTenant = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var crossTenant = await SendMcpAsync(_mcpClient, "/mcp", InitializeBody, foreignTenant, generatedKey!);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.response.StatusCode);
        Assert.Contains("Cross-tenant access forbidden", crossTenant.body);

        var splitBrain = await SendMcpAsync(_isolatedMcpClient, "/mcp", InitializeBody, tenantId, generatedKey!, origin: "https://www.cursor.com");
        Assert.Equal(HttpStatusCode.Unauthorized, splitBrain.response.StatusCode);
        Assert.Contains("Invalid or unknown tenant API key", splitBrain.body);

        var revoke = await _api.DeleteAsync($"/api/tenants/{tenantId}/keys/{generatedKeyId}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var revoked = await SendMcpAsync(_mcpClient, "/mcp", InitializeBody, tenantId, generatedKey!);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.response.StatusCode);
        Assert.Contains("Invalid or unknown tenant API key", revoked.body);
    }

    [Fact]
    public async Task Live_mcp_accepts_configured_tenant_api_key()
    {
        var url = Environment.GetEnvironmentVariable("MCP_URL");
        var apiKey = Environment.GetEnvironmentVariable("MCP_API_KEY");
        var tenantIdText = Environment.GetEnvironmentVariable("MCP_TENANT_ID");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(tenantIdText) || !Guid.TryParse(tenantIdText, out var tenantId))
        {
            _output.WriteLine("Live MCP tenant-key check skipped. Set MCP_URL, MCP_API_KEY, and MCP_TENANT_ID to verify a specific host (staging or production).");
            return;
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var initialize = await SendMcpAsync(client, url, InitializeBody, tenantId, apiKey);
        AssertMcpSuccess(initialize.response, initialize.body);

        var toolsList = await SendMcpAsync(client, url, ToolsListBody, tenantId, apiKey);
        AssertMcpSuccess(toolsList.response, toolsList.body);
        using var toolsJson = JsonDocument.Parse(toolsList.body);
        Assert.True(toolsJson.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength() > 0);
    }

    private static WebApplication BuildMcp(string databaseName, string allowedOrigins)
    {
        return FlowOS.MCP.Program.BuildHttpApp([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Environment.EnvironmentName = "Development";
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "true",
                ["MCP_API_KEY"] = PlatformMcpKey,
                ["MCP_ROLE"] = "Admin",
                ["MCP_ALLOWED_ORIGINS"] = allowedOrigins,
                ["ConnectionStrings:DefaultConnection"] = "Host=;Port=5432;Database=flowos;Username=root;Password=toor"
            });
            UseNamedInMemoryDatabase(builder.Services, databaseName);
        });
    }

    private static void UseNamedInMemoryDatabase(IServiceCollection services, string databaseName)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList())
            services.Remove(descriptor);
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList())
            services.Remove(descriptor);

        services.AddDbContext<FlowOSDbContext>(options => options.UseInMemoryDatabase(databaseName));
    }

    private static HttpRequestMessage AuthorizedApi(HttpMethod method, string path, string apiKey, Guid tenantId)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-API-Key", apiKey);
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        return request;
    }

    private static async Task<(HttpResponseMessage response, string body)> SendMcpAsync(
        HttpClient client,
        string url,
        string body,
        Guid tenantId,
        string apiKey,
        string? origin = null,
        string keyHeader = "X-MCP-API-Key")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.Add(keyHeader, apiKey);
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        request.Headers.Add("MCP-Protocol-Version", "2025-03-26");
        if (!string.IsNullOrWhiteSpace(origin))
            request.Headers.Add("Origin", origin);

        var response = await client.SendAsync(request);
        return (response, await response.Content.ReadAsStringAsync());
    }

    private static void AssertMcpSuccess(HttpResponseMessage response, string body)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("result", out _), $"MCP call failed: {body}");
        if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            Assert.Fail($"MCP JSON-RPC error: {body}");
    }
}
