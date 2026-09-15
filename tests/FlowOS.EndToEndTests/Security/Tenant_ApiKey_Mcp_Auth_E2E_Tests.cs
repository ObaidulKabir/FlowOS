using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
/// Reproduces the production failure: a tenant API key issued by the dashboard/API
/// must authenticate MCP on the same store, including Cursor Origin requests
/// when MCP_ALLOWED_ORIGINS is empty.
/// </summary>
public sealed class Tenant_ApiKey_Mcp_Auth_E2E_Tests : IAsyncLifetime
{
    private const string PlatformMcpKey = "e2e-mcp-platform-secret";
    private const string InitializeBody =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"cursor-e2e","version":"1"},"capabilities":{}}}""";

    private readonly string _sharedDb = "FlowOS_E2E_McpTenantKey_" + Guid.NewGuid().ToString("N");
    private readonly string _isolatedDb = "FlowOS_E2E_McpIsolated_" + Guid.NewGuid().ToString("N");

    private WebApplicationFactory<Program> _apiFactory = null!;
    private WebApplication _mcp = null!;
    private WebApplication _isolatedMcp = null!;
    private HttpClient _api = null!;
    private HttpClient _mcpClient = null!;
    private HttpClient _isolatedMcpClient = null!;

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
    public async Task Api_generated_tenant_key_authenticates_mcp_on_shared_store_with_cursor_origin()
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
        Assert.False(string.IsNullOrWhiteSpace(generatedKey));
        Assert.NotEqual(generateJson.RootElement.GetProperty("maskedKey").GetString(), generatedKey);

        var sharedAuth = await SendMcpInitializeAsync(_mcpClient, tenantId, generatedKey!, origin: "https://www.cursor.com");
        Assert.Equal(HttpStatusCode.OK, sharedAuth.StatusCode);
        Assert.Equal("https://www.cursor.com", sharedAuth.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var registrationAuth = await SendMcpInitializeAsync(_mcpClient, tenantId, registrationKey!, origin: "https://www.cursor.com");
        Assert.Equal(HttpStatusCode.OK, registrationAuth.StatusCode);

        var unknown = await SendMcpInitializeAsync(_mcpClient, tenantId, "flw_live_unknown_key_not_in_database");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Contains("Invalid or unknown tenant API key", await unknown.Content.ReadAsStringAsync());

        var splitBrain = await SendMcpInitializeAsync(_isolatedMcpClient, tenantId, generatedKey!, origin: "https://www.cursor.com");
        Assert.Equal(HttpStatusCode.Unauthorized, splitBrain.StatusCode);
        Assert.Contains("Invalid or unknown tenant API key", await splitBrain.Content.ReadAsStringAsync());
    }

    private static WebApplication BuildMcp(string databaseName, string allowedOrigins)
    {
        return FlowOS.MCP.Program.BuildHttpApp([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
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

    private static async Task<HttpResponseMessage> SendMcpInitializeAsync(
        HttpClient client,
        Guid tenantId,
        string apiKey,
        string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(InitializeBody, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.Add("X-MCP-API-Key", apiKey);
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        request.Headers.Add("MCP-Protocol-Version", "2025-03-26");
        if (!string.IsNullOrWhiteSpace(origin))
            request.Headers.Add("Origin", origin);

        return await client.SendAsync(request);
    }
}
