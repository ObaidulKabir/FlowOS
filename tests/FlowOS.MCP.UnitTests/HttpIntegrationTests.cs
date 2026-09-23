using System.Net;
using System.Text;
using FlowOS.MCP.Models;
using FlowOS.MCP.Server;
using FlowOS.MCP.Services;
using FlowOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
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
            builder.Environment.EnvironmentName = "Development";
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "true",
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ConnectionStrings:DefaultConnection"] = "Host=",
                ["MCP_API_KEY"] = ApiKey,
                ["MCP_ROLE"] = "Admin",
                ["MCP_ALLOWED_ORIGINS"] = "https://allowed.example",
                ["FLOWOS_BILLING_ENFORCE"] = "false"
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
        Assert.Contains("jsonrpcUrl", jsonContent);
        Assert.Contains("Do not normalize /mcp/", jsonContent);

        // HTML discovery test
        using var htmlReq = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        htmlReq.Headers.Add("Accept", "text/html");
        var htmlResponse = await _client.SendAsync(htmlReq);
        Assert.Equal(HttpStatusCode.OK, htmlResponse.StatusCode);
        var html = await htmlResponse.Content.ReadAsStringAsync();
        Assert.Contains("FlowOS MCP Control Plane", html);
        Assert.Contains("Registered Agent Tools", html);
        Assert.Contains("JSON-RPC connection", html);
        Assert.Contains("trailing slash", html);
    }

    [Fact]
    public async Task Api_key_and_tenant_are_required()
    {
        using var noKey = JsonRequest("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(noKey)).StatusCode);

        using var noTenant = JsonRequest("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        noTenant.Headers.Add("X-MCP-API-Key", ApiKey);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(noTenant)).StatusCode);

        using var unknownKey = JsonRequest("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        unknownKey.Headers.Add("X-MCP-API-Key", "flw_live_unknown_key_not_in_database");
        unknownKey.Headers.Add("x-tenant-id", TenantId.ToString());
        var unknownResponse = await _client.SendAsync(unknownKey);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownResponse.StatusCode);
        Assert.Contains("Invalid or unknown tenant API key", await unknownResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Trailing_slash_mcp_path_is_authenticated_the_same_as_mcp()
    {
        using var noKey = new HttpRequestMessage(HttpMethod.Post, "/mcp/")
        {
            Content = JsonContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""")
        };
        AddAccept(noKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(noKey)).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp/")
        {
            Content = JsonContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"tests","version":"1"},"capabilities":{}}}""")
        };
        AddAccept(request);
        Authorize(request);
        request.Headers.Add("MCP-Protocol-Version", McpJsonRpcDispatcher.SupportedProtocolVersion);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(request)).StatusCode);

        using var options = new HttpRequestMessage(HttpMethod.Options, "/mcp/");
        options.Headers.Add("Origin", "https://allowed.example");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(options)).StatusCode);
    }

    [Fact]
    public async Task Discovery_advertises_staging_or_production_origin()
    {
        await using var staging = FlowOS.MCP.Program.BuildHttpApp([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Environment.EnvironmentName = "Development";
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "true",
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ConnectionStrings:DefaultConnection"] = "Host=",
                ["MCP_API_KEY"] = ApiKey,
                ["MCP_ROLE"] = "Admin",
                ["FLOWOS_PUBLIC_ORIGIN"] = "https://flowos.prospectbdltd.com"
            });
        });
        await staging.StartAsync();
        using var stagingClient = staging.GetTestClient();
        var stagingJson = await stagingClient.GetStringAsync("/mcp");
        var stagingDoc = JObject.Parse(stagingJson);
        Assert.Equal("https://flowos.prospectbdltd.com/mcp", stagingDoc["jsonrpcUrl"]?.ToString());
        Assert.Equal("/mcp", stagingDoc["endpoint"]?.ToString());
        Assert.Equal("/mcp", stagingDoc["connection"]?["jsonrpcPath"]?.ToString());
        Assert.Contains("Do not normalize /mcp/", stagingDoc["connection"]?["rule"]?.ToString());
        Assert.Equal(false, (bool?)stagingDoc["connection"]?["followRedirectsOnPost"]);

        await using var production = FlowOS.MCP.Program.BuildHttpApp([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Environment.EnvironmentName = "Development";
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "true",
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ConnectionStrings:DefaultConnection"] = "Host=",
                ["MCP_API_KEY"] = ApiKey,
                ["MCP_ROLE"] = "Admin",
                ["FLOWOS_PUBLIC_ORIGIN"] = "https://flowosbd.com"
            });
        });
        await production.StartAsync();
        using var productionClient = production.GetTestClient();
        var productionWellKnown = JObject.Parse(await productionClient.GetStringAsync("/.well-known/mcp"));
        Assert.Equal("https://flowosbd.com/mcp/", productionWellKnown["url"]?.ToString());
        Assert.Equal("/mcp/", productionWellKnown["endpoint"]?.ToString());
        Assert.Equal("/mcp/", productionWellKnown["connection"]?["jsonrpcPath"]?.ToString());
        Assert.Contains("trailing slash", productionWellKnown["connection"]?["rule"]?.ToString());

        var productionDiscovery = JObject.Parse(await productionClient.GetStringAsync("/mcp"));
        Assert.Equal("https://flowosbd.com/mcp/", productionDiscovery["jsonrpcUrl"]?.ToString());
        Assert.Equal("/mcp/", productionDiscovery["endpoint"]?.ToString());
    }

    [Fact]
    public async Task Empty_or_wildcard_allowed_origins_accept_cursor_origin()
    {
        foreach (var allowedOrigins in new[] { "", "*" })
        {
            await using var app = FlowOS.MCP.Program.BuildHttpApp([], builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Environment.EnvironmentName = "Development";
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UseInMemoryDatabase"] = "true",
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    ["ConnectionStrings:DefaultConnection"] = "Host=",
                    ["MCP_API_KEY"] = ApiKey,
                    ["MCP_ROLE"] = "Admin",
                    ["MCP_ALLOWED_ORIGINS"] = allowedOrigins
                });
            });
            await app.StartAsync();
            using var client = app.GetTestClient();

            using var request = JsonRequest("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"cursor","version":"1"},"capabilities":{}}}""");
            Authorize(request);
            request.Headers.Add("Origin", "https://www.cursor.com");
            request.Headers.Add("MCP-Protocol-Version", McpJsonRpcDispatcher.SupportedProtocolVersion);

            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("https://www.cursor.com", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }
    }

    [Fact]
    public async Task Options_preflight_allows_api_key_headers()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/mcp");
        request.Headers.Add("Origin", "https://allowed.example");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var allowHeaders = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"));
        Assert.Contains("X-MCP-API-Key", allowHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-API-Key", allowHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorization", allowHeaders, StringComparison.OrdinalIgnoreCase);
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

        using var allowedOrigin = JsonRequest("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","clientInfo":{"name":"tests","version":"1"},"capabilities":{}}}""");
        Authorize(allowedOrigin);
        allowedOrigin.Headers.Add("Origin", "https://allowed.example");
        allowedOrigin.Headers.Add("MCP-Protocol-Version", McpJsonRpcDispatcher.SupportedProtocolVersion);
        var allowedOriginResponse = await _client.SendAsync(allowedOrigin);
        Assert.Equal(HttpStatusCode.OK, allowedOriginResponse.StatusCode);
        Assert.Equal("https://allowed.example", allowedOriginResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

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
        Assert.Equal(McpToolDescriptions.All.Count, httpNames.Length);
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
        Assert.Equal(McpToolDescriptions.All.Count, tools.Count);

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

            var requiresHumanConfirmation = tool["requiresHumanConfirmation"];
            Assert.NotNull(requiresHumanConfirmation);
            if (name is "publish_workflowclass" or "activate_context_binding" or "archive_context_binding"
                or "create_tenant_role" or "grant_role_capability" or "revoke_role_capability")
            {
                Assert.True(requiresHumanConfirmation.Value<bool>());
                Assert.NotNull(schema["properties"]?["confirmHumanApproval"]);
            }
            else
            {
                Assert.False(requiresHumanConfirmation.Value<bool>());
            }
        }

        // Formal securityContract checks in JSON
        var securityContract = json["securityContract"];
        Assert.NotNull(securityContract);
        Assert.NotNull(securityContract["riskLevels"]?["low"]);
        Assert.NotNull(securityContract["riskLevels"]?["medium"]);
        Assert.NotNull(securityContract["riskLevels"]?["high"]);
        Assert.NotNull(securityContract["sideEffects"]?["none"]);
        Assert.NotNull(securityContract["sideEffects"]?["reversible"]);
        Assert.NotNull(securityContract["sideEffects"]?["irreversible"]);

        // Also verify HTML discovery rendering
        using var htmlReq = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        htmlReq.Headers.Add("Accept", "text/html");
        var htmlResponse = await _client.SendAsync(htmlReq);
        var html = await htmlResponse.Content.ReadAsStringAsync();
        Assert.Contains("Argument Schema (inputSchema)", html);
        Assert.Contains("Risk:", html);
        Assert.Contains("Agent Governance & Security Policy", html);
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

    [Fact]
    public async Task Run_agent_task_claims_and_completes_durable_job_synchronously()
    {
        Guid instanceId;
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var definition = new FlowOS.Workflows.Domain.WorkflowDefinition(
                TenantId,
                $"McpDurableAgent{Guid.NewGuid():N}",
                1,
                "AgentReview");
            definition.AddStep(new FlowOS.Workflows.Domain.WorkflowStepDefinition(
                "AgentReview",
                FlowOS.Workflows.Enums.WorkflowStepType.HumanTask)
            {
                Actor = FlowOS.Domain.Enums.StepActor.Agent,
                AgentProvider = FlowOS.Core.Common.Interfaces.AgentProviderKinds.FlowosRisk,
                DecisionGuideline = "Choose a legal event only when risk facts justify it.",
                NextSteps = new Dictionary<string, string>
                {
                    ["APPROVE"] = "END"
                }
            });
            definition.Publish();
            var instance = new FlowOS.Workflows.Domain.WorkflowInstance(
                TenantId,
                definition.Id,
                Guid.Empty,
                definition.Version,
                "AgentReview");
            instance.Wait();
            db.AddRange(definition, instance);
            await db.SaveChangesAsync();
            instanceId = instance.Id;
        }

        var response = await SendAsync(
            $$"""
            {
              "jsonrpc":"2.0",
              "id":77,
              "method":"tools/call",
              "params":{
                "name":"run_agent_task",
                "arguments":{"workflowInstanceId":"{{instanceId}}"}
              }
            }
            """);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json["result"]?["isError"]?.Value<bool>(), json.ToString());
        var envelope = JObject.Parse(
            json["result"]?["content"]?[0]?["text"]?.ToString() ?? "{}");
        var data = Assert.IsType<JObject>(envelope["data"]);
        var jobId = Guid.Parse(data["jobId"]!.ToString());
        Assert.True(Guid.TryParse(data["executionId"]?.ToString(), out _));

        using var verificationScope = _app.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider
            .GetRequiredService<FlowOSDbContext>();
        var job = await verificationDb.AgentTaskJobs
            .AsNoTracking()
            .SingleAsync(x => x.Id == jobId);
        var execution = await verificationDb.AgentExecutionRecords
            .AsNoTracking()
            .SingleAsync(x => x.JobId == jobId);
        Assert.Equal(
            FlowOS.Domain.Enums.AgentTaskJobStatus.Completed,
            job.Status);
        Assert.StartsWith("mcp-run:", execution.Claimant);
    }

    [Fact]
    public async Task Cross_tenant_object_level_IDOR_cannot_access_foreign_resources()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string tenantAKey = "flw_live_tenant_a_idor_key_99999";

        Guid tenantBPrivateDraftId;
        Guid tenantBWorkflowInstanceId;

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOS.Infrastructure.Persistence.FlowOSDbContext>();
            
            // Seed Tenant A API key and Admin role with workflow.start permission
            db.TenantApiKeys.Add(new FlowOS.Domain.Entities.TenantApiKey(tenantA, "Tenant A Key", tenantAKey));
            var adminRole = new FlowOS.Security.Models.Role(tenantA, "Admin");
            adminRole.AddPermission("workflow.start");
            db.Roles.Add(adminRole);

            // Seed Tenant B Private WorkflowClass
            var blueprint = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint
            {
                Events = new() { new() { EventId = "EVT-DONE", Name = "Done" } },
                StateMachine = new() { InitialState = "S1", States = new() { "S1" } },
                Workflow = new()
                {
                    StartStepId = "S1",
                    Steps = new()
                    {
                        new()
                        {
                            StepId = "S1",
                            StepType = "Command",
                            NextSteps = new() { { "EVT-DONE", "END" } }
                        }
                    }
                }
            };
            var tenantBClass = new FlowOS.Domain.Entities.WorkflowClass(tenantB, "TenantBSecretWorkflow", "1.0.0", blueprint);
            db.WorkflowClasses.Add(tenantBClass);
            tenantBPrivateDraftId = tenantBClass.Id;

            // Seed Tenant B WorkflowInstance
            var tenantBInstance = new FlowOS.Workflows.Domain.WorkflowInstance(
                tenantB, Guid.NewGuid(), tenantBPrivateDraftId, 1, "S1", Guid.NewGuid());
            db.WorkflowInstances.Add(tenantBInstance);
            tenantBWorkflowInstanceId = tenantBInstance.Id;

            await db.SaveChangesAsync();
        }

        async Task<JObject> CallToolAsTenantA(string toolName, object arguments)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = JsonContent($$"""
                {
                  "jsonrpc": "2.0",
                  "id": 1,
                  "method": "tools/call",
                  "params": {
                    "name": "{{toolName}}",
                    "arguments": {{JsonConvert.SerializeObject(arguments)}}
                  }
                }
                """)
            };
            req.Headers.Add("X-MCP-API-Key", tenantAKey);
            req.Headers.Add("x-tenant-id", tenantA.ToString());
            req.Headers.Add("MCP-Protocol-Version", "2025-03-26");
            AddAccept(req);

            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var content = await res.Content.ReadAsStringAsync();
            return JObject.Parse(content);
        }

        // 1. Tenant A attempts to get Tenant B's private draft
        var getDraftRes = await CallToolAsTenantA("get_draft_workflowclass", new { id = tenantBPrivateDraftId });
        Assert.True(getDraftRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-NOTFOUND-001", getDraftRes.ToString());
        Assert.DoesNotContain("TenantBSecretWorkflow", getDraftRes.ToString());

        // 2. Tenant A attempts to update Tenant B's private draft
        var updateDraftRes = await CallToolAsTenantA("update_draft_workflowclass", new
        {
            id = tenantBPrivateDraftId,
            name = "HackedName",
            blueprint = new { }
        });
        Assert.True(updateDraftRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-NOTFOUND-001", updateDraftRes.ToString());

        // 3. Tenant A attempts to lint Tenant B's private draft
        var lintDraftRes = await CallToolAsTenantA("lint_draft_workflowclass", new { id = tenantBPrivateDraftId });
        Assert.True(lintDraftRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-NOTFOUND-001", lintDraftRes.ToString());

        // 4. Tenant A attempts to publish Tenant B's private draft (with confirmation) -> blocked by tenant boundary
        var publishRes = await CallToolAsTenantA("publish_workflowclass", new { id = tenantBPrivateDraftId, confirmHumanApproval = true });
        Assert.True(publishRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-NOTFOUND-001", publishRes.ToString());

        // 5. Tenant A attempts to start workflow with Tenant B's private workflowClassId
        var startRes = await CallToolAsTenantA("start_workflow", new { workflowClassId = tenantBPrivateDraftId });
        Assert.True(startRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-NOTFOUND-001", startRes.ToString());
        Assert.DoesNotContain("TenantBSecretWorkflow", startRes.ToString());

        // 6. Tenant A attempts to query workflow history of Tenant B's instance
        var historyRes = await CallToolAsTenantA("get_workflow_history", new { workflowInstanceId = tenantBWorkflowInstanceId });
        Assert.True(historyRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-NOTFOUND-001", historyRes.ToString());

        // 7. Tenant A attempts to query status of Tenant B's instance
        var statusRes = await CallToolAsTenantA("get_workflow_instance_status", new { instanceId = tenantBWorkflowInstanceId });
        Assert.True(statusRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-NOTFOUND-001", statusRes.ToString());

        // 8. Tenant A attempts to run advisory agent against Tenant B's instance
        var agentRes = await CallToolAsTenantA("suggest_agent_action", new
        {
            workflowInstanceId = tenantBWorkflowInstanceId,
            agentId = "RiskAnalysisAgent"
        });
        Assert.True(agentRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-NOTFOUND-001", agentRes.ToString());
    }

    [Fact]
    public async Task High_risk_irreversible_action_enforces_human_confirmation_policy()
    {
        var tenantA = Guid.NewGuid();
        const string tenantAKey = "flw_live_tenant_a_policy_key_11111";
        Guid draftId;

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOS.Infrastructure.Persistence.FlowOSDbContext>();
            db.TenantApiKeys.Add(new FlowOS.Domain.Entities.TenantApiKey(tenantA, "Tenant A Key", tenantAKey));
            
            var blueprint = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint
            {
                Events = new() { new() { EventId = "EVT-DONE", Name = "Done" } },
                StateMachine = new() { InitialState = "S1", States = new() { "S1" } },
                Workflow = new()
                {
                    StartStepId = "S1",
                    Steps = new()
                    {
                        new()
                        {
                            StepId = "S1",
                            StepType = "Command",
                            NextSteps = new() { { "EVT-DONE", "END" } }
                        }
                    }
                }
            };
            var draft = new FlowOS.Domain.Entities.WorkflowClass(tenantA, "PolicyGateWorkflow", "1.0.0", blueprint);
            db.WorkflowClasses.Add(draft);
            draftId = draft.Id;
            await db.SaveChangesAsync();
        }

        async Task<JObject> CallPublish(bool? confirmApproval)
        {
            var argsObj = confirmApproval.HasValue
                ? (object)new { id = draftId, confirmHumanApproval = confirmApproval.Value }
                : new { id = draftId };

            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = JsonContent($$"""
                {
                  "jsonrpc": "2.0",
                  "id": 1,
                  "method": "tools/call",
                  "params": {
                    "name": "publish_workflowclass",
                    "arguments": {{JsonConvert.SerializeObject(argsObj)}}
                  }
                }
                """)
            };
            req.Headers.Add("X-MCP-API-Key", tenantAKey);
            req.Headers.Add("x-tenant-id", tenantA.ToString());
            req.Headers.Add("MCP-Protocol-Version", "2025-03-26");
            AddAccept(req);

            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return JObject.Parse(await res.Content.ReadAsStringAsync());
        }

        // 1. Without explicit confirmation -> rejected with MCP-APPROVAL-REQUIRED
        var rejectedRes = await CallPublish(null);
        Assert.True(rejectedRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-APPROVAL-REQUIRED", rejectedRes.ToString());

        var rejectedFalseRes = await CallPublish(false);
        Assert.True(rejectedFalseRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-APPROVAL-REQUIRED", rejectedFalseRes.ToString());

        // 2. With explicit confirmHumanApproval: true -> succeeds
        var approvedRes = await CallPublish(true);
        Assert.False(approvedRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("Published", approvedRes.ToString());
    }

    [Fact]
    public async Task Public_workflow_classes_are_accessible_cross_tenant_while_private_remain_isolated()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string tenantAKey = "flw_live_tenant_a_public_access_key_22222";

        Guid tenantBPublicClassId;
        Guid tenantBPrivateClassId;

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOS.Infrastructure.Persistence.FlowOSDbContext>();
            db.TenantApiKeys.Add(new FlowOS.Domain.Entities.TenantApiKey(tenantA, "Tenant A Key", tenantAKey));
            var adminRole = new FlowOS.Security.Models.Role(tenantA, "Admin");
            adminRole.AddPermission("workflow.start");
            db.Roles.Add(adminRole);

            var blueprint = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint
            {
                Events = new() { new() { EventId = "EVT-DONE", Name = "Done" } },
                StateMachine = new() { InitialState = "S1", States = new() { "S1" } },
                Workflow = new()
                {
                    StartStepId = "S1",
                    Steps = new()
                    {
                        new()
                        {
                            StepId = "S1",
                            StepType = "Command",
                            NextSteps = new() { { "EVT-DONE", "END" } }
                        }
                    }
                }
            };

            // Public WorkflowClass owned by Tenant B
            var publicClass = new FlowOS.Domain.Entities.WorkflowClass(tenantB, "FleetSharedBlueprint", "1.0.0", blueprint);
            var manager = new FlowOS.Domain.Services.WorkflowClassManager();
            manager.Publish(publicClass);
            manager.SubmitForReview(publicClass);
            manager.ApproveAsPublic(publicClass);
            db.WorkflowClasses.Add(publicClass);
            tenantBPublicClassId = publicClass.Id;

            // Runtime WorkflowDefinition for the public class
            var runtimeDef = FlowOS.Application.Services.WorkflowClassCompiler.MapToRuntimeDefinition(publicClass);
            runtimeDef.Publish();
            db.WorkflowDefinitions.Add(runtimeDef);

            // Private WorkflowClass owned by Tenant B
            var privateClass = new FlowOS.Domain.Entities.WorkflowClass(tenantB, "SecretInternalBlueprint", "1.0.0", blueprint);
            db.WorkflowClasses.Add(privateClass);
            tenantBPrivateClassId = privateClass.Id;

            await db.SaveChangesAsync();
        }

        async Task<JObject> CallTool(string toolName, object args)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = JsonContent($$"""
                {
                  "jsonrpc": "2.0",
                  "id": 1,
                  "method": "tools/call",
                  "params": {
                    "name": "{{toolName}}",
                    "arguments": {{JsonConvert.SerializeObject(args)}}
                  }
                }
                """)
            };
            req.Headers.Add("X-MCP-API-Key", tenantAKey);
            req.Headers.Add("x-tenant-id", tenantA.ToString());
            req.Headers.Add("MCP-Protocol-Version", "2025-03-26");
            AddAccept(req);

            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return JObject.Parse(await res.Content.ReadAsStringAsync());
        }

        // 1. Tenant A forks Tenant B's public workflow class -> allowed
        var forkRes = await CallTool("fork_public_workflowclass", new { publicId = tenantBPublicClassId });
        Assert.False(forkRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("Forked from FleetSharedBlueprint", forkRes.ToString());

        // 2. Tenant A starts an instance of Tenant B's public workflow class -> allowed
        var startRes = await CallTool("start_workflow", new { workflowClassId = tenantBPublicClassId });
        Assert.False(startRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("Running", startRes.ToString());

        // 3. Tenant A attempts to access Tenant B's private workflow class -> rejected (not found)
        var privateRes = await CallTool("get_draft_workflowclass", new { id = tenantBPrivateClassId });
        Assert.True(privateRes["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-NOTFOUND-001", privateRes.ToString());
    }

    [Fact]
    public async Task Side_channel_uniformity_for_foreign_vs_nonexistent_ids()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string tenantAKey = "flw_live_tenant_a_timing_key_33333";

        Guid tenantBPrivateClassId;
        Guid tenantBWorkflowInstanceId;

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOS.Infrastructure.Persistence.FlowOSDbContext>();
            db.TenantApiKeys.Add(new FlowOS.Domain.Entities.TenantApiKey(tenantA, "Tenant A Key", tenantAKey));

            var blueprint = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint
            {
                Events = new() { new() { EventId = "EVT-DONE", Name = "Done" } },
                StateMachine = new() { InitialState = "S1", States = new() { "S1" } },
                Workflow = new()
                {
                    StartStepId = "S1",
                    Steps = new()
                    {
                        new()
                        {
                            StepId = "S1",
                            StepType = "Command",
                            NextSteps = new() { { "EVT-DONE", "END" } }
                        }
                    }
                }
            };
            var privateClass = new FlowOS.Domain.Entities.WorkflowClass(tenantB, "SecretTimingBlueprint", "1.0.0", blueprint);
            db.WorkflowClasses.Add(privateClass);
            tenantBPrivateClassId = privateClass.Id;

            var instance = new FlowOS.Workflows.Domain.WorkflowInstance(
                tenantB, Guid.NewGuid(), tenantBPrivateClassId, 1, "S1", Guid.NewGuid());
            db.WorkflowInstances.Add(instance);
            tenantBWorkflowInstanceId = instance.Id;

            await db.SaveChangesAsync();
        }

        async Task<(HttpStatusCode status, JObject body)> CallTool(string toolName, object args)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = JsonContent($$"""
                {
                  "jsonrpc": "2.0",
                  "id": 1,
                  "method": "tools/call",
                  "params": {
                    "name": "{{toolName}}",
                    "arguments": {{JsonConvert.SerializeObject(args)}}
                  }
                }
                """)
            };
            req.Headers.Add("X-MCP-API-Key", tenantAKey);
            req.Headers.Add("x-tenant-id", tenantA.ToString());
            req.Headers.Add("MCP-Protocol-Version", "2025-03-26");
            AddAccept(req);

            var res = await _client.SendAsync(req);
            var parsed = JObject.Parse(await res.Content.ReadAsStringAsync());
            return (res.StatusCode, parsed);
        }

        var nonexistentId = Guid.NewGuid();

        // 1. Compare get_draft_workflowclass on nonexistent ID vs. foreign private ID
        var (nonExistentDraftStatus, nonExistentDraftBody) = await CallTool("get_draft_workflowclass", new { id = nonexistentId });
        var (foreignDraftStatus, foreignDraftBody) = await CallTool("get_draft_workflowclass", new { id = tenantBPrivateClassId });

        Assert.Equal(nonExistentDraftStatus, foreignDraftStatus);
        Assert.Equal(nonExistentDraftBody["result"]?["isError"]?.Value<bool>(), foreignDraftBody["result"]?["isError"]?.Value<bool>());
        Assert.Equal(
            nonExistentDraftBody["result"]?["content"]?[0]?["text"]?.ToString(),
            foreignDraftBody["result"]?["content"]?[0]?["text"]?.ToString());

        // 2. Compare get_workflow_history on nonexistent ID vs. foreign instance ID
        var (nonExistentHistStatus, nonExistentHistBody) = await CallTool("get_workflow_history", new { workflowInstanceId = nonexistentId });
        var (foreignHistStatus, foreignHistBody) = await CallTool("get_workflow_history", new { workflowInstanceId = tenantBWorkflowInstanceId });

        Assert.Equal(nonExistentHistStatus, foreignHistStatus);
        Assert.Equal(nonExistentHistBody["result"]?["isError"]?.Value<bool>(), foreignHistBody["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-NOTFOUND-001", nonExistentHistBody.ToString());
        Assert.Contains("MCP-NOTFOUND-001", foreignHistBody.ToString());
    }

    [Fact]
    public async Task New_features_tools_execute_over_http_successfully()
    {
        // 1. simulate_parallel_execution with inline Fork/Join blueprint
        var forkJoinBlueprint = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint
        {
            Events = new() { new() { EventId = "EVT-SUBMIT", Name = "Submit" } },
            StateMachine = new()
            {
                InitialState = "Submitted",
                States = new() { "Submitted", "Approved" }
            },
            Workflow = new()
            {
                StartStepId = "StartFork",
                Steps = new()
                {
                    new()
                    {
                        StepId = "StartFork",
                        StepType = "Fork",
                        Branches = new() { "BranchA", "BranchB" }
                    },
                    new()
                    {
                        StepId = "BranchA",
                        StepType = "Command",
                        NextSteps = new() { { "Default", "SyncJoin" } }
                    },
                    new()
                    {
                        StepId = "BranchB",
                        StepType = "Command",
                        NextSteps = new() { { "Default", "SyncJoin" } }
                    },
                    new()
                    {
                        StepId = "SyncJoin",
                        StepType = "Join",
                        JoinPolicy = "WaitAll",
                        NextSteps = new() { { "Default", "Finish" } }
                    },
                    new()
                    {
                        StepId = "Finish",
                        StepType = "End"
                    }
                }
            }
        };

        var simCall = await SendAsync($$"""
        {
          "jsonrpc": "2.0",
          "id": 101,
          "method": "tools/call",
          "params": {
            "name": "simulate_parallel_execution",
            "arguments": {
              "blueprint": {{JsonConvert.SerializeObject(forkJoinBlueprint)}},
              "payload": { "amount": 1000 }
            }
          }
        }
        """);

        Assert.Equal(HttpStatusCode.OK, simCall.StatusCode);
        var simJson = JObject.Parse(await simCall.Content.ReadAsStringAsync());
        Assert.False(simJson["result"]?["isError"]?.Value<bool>());
        var simPayload = JObject.Parse(simJson["result"]?["content"]?[0]?["text"]?.ToString() ?? "{}");
        Assert.True(simPayload["ok"]?.Value<bool>());
        var simData = simPayload["data"];
        Assert.NotNull(simData);
        Assert.Equal("StartFork", simData["forkStepId"]?.ToString());
        Assert.Equal(2, simData["totalBranches"]?.Value<int>());
        Assert.True(simData["isSynchronized"]?.Value<bool>());

        // 2. refine_workflow_blueprint_from_nl
        var refineCall = await SendAsync($$"""
        {
          "jsonrpc": "2.0",
          "id": 102,
          "method": "tools/call",
          "params": {
            "name": "refine_workflow_blueprint_from_nl",
            "arguments": {
              "prompt": "Add 24h SLA timeout and manager escalation webhook",
              "currentBlueprint": {{JsonConvert.SerializeObject(forkJoinBlueprint)}}
            }
          }
        }
        """);

        Assert.Equal(HttpStatusCode.OK, refineCall.StatusCode);
        var refineJson = JObject.Parse(await refineCall.Content.ReadAsStringAsync());
        Assert.False(refineJson["result"]?["isError"]?.Value<bool>());
        var refinePayload = JObject.Parse(refineJson["result"]?["content"]?[0]?["text"]?.ToString() ?? "{}");
        Assert.True(refinePayload["ok"]?.Value<bool>());
        Assert.NotNull(refinePayload["data"]?["Blueprint"] ?? refinePayload["data"]?["blueprint"]);

        // 3. get_subworkflow_tree for non-existent instance -> MCP-NOTFOUND-001
        var treeCall = await SendAsync($$"""
        {
          "jsonrpc": "2.0",
          "id": 103,
          "method": "tools/call",
          "params": {
            "name": "get_subworkflow_tree",
            "arguments": {
              "workflowInstanceId": "{{Guid.NewGuid()}}"
            }
          }
        }
        """);

        Assert.Equal(HttpStatusCode.OK, treeCall.StatusCode);
        var treeJson = JObject.Parse(await treeCall.Content.ReadAsStringAsync());
        Assert.True(treeJson["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-NOTFOUND-001", treeJson.ToString());
    }

    [Fact]
    public async Task Context_binding_create_validate_activate_start_and_publish_sequence_works_over_http()
    {
        Guid sourceId;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var contextType = $"McpExpense{suffix}";

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOS.Infrastructure.Persistence.FlowOSDbContext>();
            var blueprint = new FlowOS.Domain.Blueprints.WorkflowClassBlueprint
            {
                ContextSchema = """{"type":"object","required":["Amount","ApprovalLimit"],"properties":{"Amount":{"type":"number"},"ApprovalLimit":{"type":"number"}}}""",
                Events =
                [
                    new()
                    {
                        EventId = "EVT-APPROVE",
                        Name = "Approve",
                        Category = FlowOS.Domain.Enums.EventCategory.Human,
                        RequiredCapabilities = ["event.publish.EVT-APPROVE"]
                    }
                ],
                StateMachine = new()
                {
                    EntityType = "ApprovalSubject",
                    InitialState = "Draft",
                    States = ["Draft", "Approved"],
                    Transitions =
                    [
                        new()
                        {
                            FromState = "Draft",
                            ToState = "Approved",
                            EventId = "EVT-APPROVE",
                            Condition = "Amount <= ApprovalLimit"
                        }
                    ]
                },
                Workflow = new()
                {
                    StartStepId = "Review",
                    Steps =
                    [
                        new()
                        {
                            StepId = "Review",
                            StepType = "HumanTask",
                            RequiredRoles = ["Approver"],
                            RequiredCapabilities = ["event.publish.EVT-APPROVE"],
                            NextSteps = new() { ["EVT-APPROVE"] = "END" }
                        }
                    ]
                },
                Roles = [new() { Name = "Approver", GrantedCapabilities = ["event.publish.EVT-APPROVE"] }],
                Capabilities = [new() { Code = "event.publish.EVT-APPROVE" }]
            };
            var source = new FlowOS.Domain.Entities.WorkflowClass(
                TenantId,
                $"McpReusableApproval{suffix}",
                "1.0.0",
                blueprint);
            Assert.True(new FlowOS.Domain.Services.WorkflowClassManager().Publish(source).IsValid);
            db.WorkflowClasses.Add(source);

            var admin = await db.Roles.FirstOrDefaultAsync(x => x.TenantId == TenantId && x.Name == "Admin");
            if (admin == null)
            {
                admin = new FlowOS.Security.Models.Role(TenantId, "Admin");
                db.Roles.Add(admin);
            }
            admin.AddPermission("workflow.start");
            admin.AddPermission("event.publish");
            var financeManager = new FlowOS.Security.Models.Role(TenantId, $"FinanceManager{suffix}");
            financeManager.AddPermission("event.publish.EVT-APPROVE");
            financeManager.AddPermission($"event.publish.EVT-MCP-APPROVE-{suffix}");
            db.Roles.Add(financeManager);
            await db.SaveChangesAsync();
            sourceId = source.Id;
        }

        async Task<JObject> CallTool(string name, object arguments)
        {
            var response = await SendAsync($$"""
            {
              "jsonrpc":"2.0",
              "id":901,
              "method":"tools/call",
              "params":{
                "name":"{{name}}",
                "arguments":{{JsonConvert.SerializeObject(arguments)}}
              }
            }
            """);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JObject.Parse(await response.Content.ReadAsStringAsync());
        }

        static JObject Data(JObject response)
        {
            Assert.False(
                response["result"]?["isError"]?.Value<bool>(),
                response.ToString());
            var payload = JObject.Parse(response["result"]?["content"]?[0]?["text"]?.ToString() ?? "{}");
            Assert.True(payload["ok"]?.Value<bool>());
            return Assert.IsType<JObject>(payload["data"]);
        }

        var created = Data(await CallTool("create_context_binding", new
        {
            sourceWorkflowClassId = sourceId,
            contextType,
            name = $"McpExpenseApproval{suffix}",
            definition = new
            {
                entityType = "ExpenseEntity",
                eventAliases = new Dictionary<string, string>
                {
                    ["EVT-APPROVE"] = $"EVT-MCP-APPROVE-{suffix}"
                },
                roleOverrides = new Dictionary<string, string>
                {
                    ["Approver"] = $"FinanceManager{suffix}"
                },
                inputMapping = new Dictionary<string, string> { ["Amount"] = "expense.amount" },
                conditionParameters = new Dictionary<string, object> { ["ApprovalLimit"] = 500 }
            }
        }));
        var bindingId = Guid.Parse((created["Id"] ?? created["id"])!.ToString());

        var invalidSimulationSelector = await CallTool("simulate_context_binding", new
        {
            contextBindingId = bindingId,
            contextType,
            revision = "draft"
        });
        Assert.True(invalidSimulationSelector["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-ARG-001", invalidSimulationSelector.ToString());

        var draftSimulation = Data(await CallTool("simulate_context_binding", new
        {
            contextBindingId = bindingId,
            revision = "draft",
            initialPayload = new { expense = new { amount = 125 } },
            roles = new[] { $"FinanceManager{suffix}" },
            events = new[] { new { eventType = $"EVT-MCP-APPROVE-{suffix}" } }
        }));
        Assert.Equal("Completed", (draftSimulation["Status"] ?? draftSimulation["status"])!.ToString());
        Assert.True((draftSimulation["SideEffectsSuppressed"] ?? draftSimulation["sideEffectsSuppressed"])!.Value<bool>());
        Assert.False((draftSimulation["IsPersistedRuntime"] ?? draftSimulation["isPersistedRuntime"])!.Value<bool>());

        var validation = Data(await CallTool("validate_context_binding", new { id = bindingId }));
        Assert.True((validation["IsValid"] ?? validation["isValid"])!.Value<bool>());

        var activateWithoutApproval = await CallTool(
            "activate_context_binding",
            new { id = bindingId });
        Assert.True(activateWithoutApproval["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-APPROVAL-REQUIRED", activateWithoutApproval.ToString());

        var activateWithFalseApproval = await CallTool(
            "activate_context_binding",
            new { id = bindingId, confirmHumanApproval = false });
        Assert.True(activateWithFalseApproval["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-APPROVAL-REQUIRED", activateWithFalseApproval.ToString());

        _ = Data(await CallTool("activate_context_binding", new
        {
            id = bindingId,
            confirmHumanApproval = true
        }));

        var activeSimulation = Data(await CallTool("simulate_context_binding", new
        {
            contextType,
            revision = "active",
            initialPayload = new { expense = new { amount = 125 } },
            roles = new[] { $"FinanceManager{suffix}" },
            events = new[] { new { eventType = $"EVT-MCP-APPROVE-{suffix}" } }
        }));
        Assert.True((activeSimulation["IsPersistedRuntime"] ?? activeSimulation["isPersistedRuntime"])!.Value<bool>());
        Assert.Equal("active", (activeSimulation["RevisionKind"] ?? activeSimulation["revisionKind"])!.ToString());

        var invalidVersionSelector = await CallTool("start_workflow", new
        {
            contextType,
            version = 1,
            payload = new { expense = new { amount = 125 } }
        });
        Assert.True(invalidVersionSelector["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-ARG-001", invalidVersionSelector.ToString());

        var invalidPayload = await CallTool("start_workflow", new
        {
            contextType,
            payload = new { expense = new { description = "Missing amount" } }
        });
        Assert.True(invalidPayload["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-VALIDATION", invalidPayload.ToString());

        var started = Data(await CallTool("start_workflow", new
        {
            contextType,
            payload = new { expense = new { amount = 125 } },
            businessReference = new { sourceSystem = "MCP-ERP", externalEntityId = $"EXP-{suffix}" }
        }));
        var instanceId = Guid.Parse(
            (started["workflowInstanceId"] ?? started["WorkflowInstanceId"])!.ToString());

        _ = Data(await CallTool("publish_event", new
        {
            workflowInstanceId = instanceId,
            eventType = $"EVT-MCP-APPROVE-{suffix}"
        }));

        var archiveWithoutApproval = await CallTool(
            "archive_context_binding",
            new { id = bindingId });
        Assert.True(archiveWithoutApproval["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-APPROVAL-REQUIRED", archiveWithoutApproval.ToString());

        var archiveWithFalseApproval = await CallTool(
            "archive_context_binding",
            new { id = bindingId, confirmHumanApproval = false });
        Assert.True(archiveWithFalseApproval["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-APPROVAL-REQUIRED", archiveWithFalseApproval.ToString());

        _ = Data(await CallTool("archive_context_binding", new
        {
            id = bindingId,
            confirmHumanApproval = true
        }));

        var archivedStart = await CallTool("start_workflow", new
        {
            contextType,
            payload = new { expense = new { amount = 125 } }
        });
        Assert.True(archivedStart["result"]?["isError"]?.Value<bool>());
        Assert.Contains("MCP-NOTFOUND-001", archivedStart.ToString());

        using var verificationScope = _app.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider
            .GetRequiredService<FlowOS.Infrastructure.Persistence.FlowOSDbContext>();
        var snapshot = await verificationDb.WorkflowContextSnapshots
            .AsNoTracking()
            .SingleAsync(x => x.WorkflowInstanceId == instanceId);
        Assert.Equal("MCP-ERP", snapshot.SourceSystem);
        Assert.Equal($"EXP-{suffix}", snapshot.ExternalEntityId);
    }

    [Fact]
    public async Task Subworkflow_simulation_via_simulate_workflowclass_executes_child_and_maps_payload()
    {
        var call = await SendAsync(
            $$"""
            {
              "jsonrpc": "2.0",
              "id": 101,
              "method": "tools/call",
              "params": {
                "name": "simulate_workflowclass",
                "arguments": {
                  "blueprint": {
                    "workflow": {
                      "startStepId": "ParentStart",
                      "steps": [
                        {
                          "stepId": "ParentStart",
                          "stepType": "Command",
                          "nextSteps": { "Default": "ExecuteChild" }
                        },
                        {
                          "stepId": "ExecuteChild",
                          "stepType": "SubWorkflow",
                          "subWorkflow": {
                            "workflowName": "ChildApprovalWorkflow",
                            "inputMapping": {
                              "RequestedAmount": "OrderAmount"
                            },
                            "outputMapping": {
                              "OrderApproved": "ChildApproved",
                              "ApprovalRisk": "RiskScore"
                            }
                          },
                          "nextSteps": {
                            "SubWorkflowCompleted": "FinalCheck"
                          }
                        },
                        {
                          "stepId": "FinalCheck",
                          "stepType": "Decision",
                          "conditions": {
                            "OrderApproved == true": "END",
                            "Default": "ManualReview"
                          }
                        },
                        {
                          "stepId": "ManualReview",
                          "stepType": "Command",
                          "nextSteps": { "Default": "END" }
                        }
                      ]
                    }
                  },
                  "subWorkflows": {
                    "ChildApprovalWorkflow": {
                      "workflow": {
                        "startStepId": "EvaluateRisk",
                        "steps": [
                          {
                            "stepId": "EvaluateRisk",
                            "stepType": "Command",
                            "onExit": [
                              {
                                "actionType": "Notification",
                                "target": "Auditor",
                                "payloadMapping": {
                                  "ChildApproved": "true",
                                  "RiskScore": "15"
                                }
                              }
                            ],
                            "nextSteps": { "Default": "END" }
                          }
                        ]
                      }
                    }
                  },
                  "payload": {
                    "OrderAmount": 500
                  }
                }
              }
            }
            """);

        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        var json = JObject.Parse(await call.Content.ReadAsStringAsync());
        Assert.False(json["result"]?["isError"]?.Value<bool>());
        var contentText = json["result"]?["content"]?[0]?["text"]?.ToString();
        Assert.NotNull(contentText);
        var payloadObj = JObject.Parse(contentText);
        var data = payloadObj["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("END", data["currentStepId"]?.ToString());
        Assert.True(data["payload"]?["SubWorkflowCompleted"]?.Value<bool>());
        Assert.True(data["payload"]?["OrderApproved"]?.Value<bool>());

        var subworkflows = data["subworkflowsExecuted"] as JArray;
        Assert.NotNull(subworkflows);
        Assert.Single(subworkflows);
        var childInfo = subworkflows[0] as JObject;
        Assert.NotNull(childInfo);
        Assert.Equal("ChildApprovalWorkflow", childInfo["childWorkflow"]?.ToString());
        Assert.Equal("Completed", childInfo["childStatus"]?.ToString());
    }

    [Fact]
    public async Task Subworkflow_simulation_via_simulate_subworkflow_tool_call()
    {
        var call = await SendAsync(
            $$"""
            {
              "jsonrpc": "2.0",
              "id": 102,
              "method": "tools/call",
              "params": {
                "name": "simulate_subworkflow",
                "arguments": {
                  "parentBlueprint": {
                    "workflow": {
                      "startStepId": "SubWorkflowStep",
                      "steps": [
                        {
                          "stepId": "SubWorkflowStep",
                          "stepType": "SubWorkflow",
                          "subWorkflow": {
                            "workflowName": "CreditCheckChild",
                            "inputMapping": {
                              "Total": "ParentTotal"
                            },
                            "outputMapping": {
                              "ParentDecision": "ChildDecision"
                            }
                          },
                          "nextSteps": {
                            "SubWorkflowCompleted": "END"
                          }
                        }
                      ]
                    }
                  },
                  "childBlueprint": {
                    "name": "CreditCheckChild",
                    "workflow": {
                      "startStepId": "RunCheck",
                      "steps": [
                        {
                          "stepId": "RunCheck",
                          "stepType": "Command",
                          "onExit": [
                            {
                              "actionType": "Notification",
                              "target": "Compliance",
                              "payloadMapping": {
                                "ChildDecision": "'APPROVED'"
                              }
                            }
                          ],
                          "nextSteps": { "Default": "END" }
                        }
                      ]
                    }
                  },
                  "payload": {
                    "ParentTotal": 1250
                  }
                }
              }
            }
            """);

        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        var json = JObject.Parse(await call.Content.ReadAsStringAsync());
        Assert.False(json["result"]?["isError"]?.Value<bool>());
        var contentText = json["result"]?["content"]?[0]?["text"]?.ToString();
        Assert.NotNull(contentText);
        var payloadObj = JObject.Parse(contentText);
        var data = payloadObj["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Completed", data["status"]?.ToString());
        Assert.Equal("SubWorkflowStep", data["subWorkflowStepId"]?.ToString());
        Assert.Equal("CreditCheckChild", data["childWorkflow"]?.ToString());
        Assert.Equal("APPROVED", data["updatedParentPayload"]?["ParentDecision"]?.ToString());
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
