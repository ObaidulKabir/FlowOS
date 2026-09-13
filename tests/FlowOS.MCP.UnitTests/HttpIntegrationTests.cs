using System.Net;
using System.Text;
using FlowOS.MCP.Server;
using FlowOS.MCP.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
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
        Assert.Equal(33, httpNames.Length);
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
        Assert.Equal(33, tools.Count);

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
            if (name == "publish_workflowclass")
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
