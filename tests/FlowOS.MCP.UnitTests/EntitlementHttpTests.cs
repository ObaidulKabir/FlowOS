using System.Net;
using System.Text;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.MCP.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

public sealed class EntitlementHttpTests
{
    private const string ApiKey = "entitlement-test-secret";

    [Fact]
    public async Task Development_DoesNotReturn402_ForTrialStartWorkflow()
    {
        await using var app = BuildApp(enforceBilling: false);
        await app.StartAsync();
        using var client = app.GetTestClient();
        var tenant = await SeedTrialTenantAsync(app);

        var (status, body) = await CallTool(client, tenant.TenantId, "start_workflow", new { workflowClassId = Guid.NewGuid() });

        Assert.NotEqual(HttpStatusCode.PaymentRequired, status);
        Assert.DoesNotContain(TenantEntitlementPolicy.PlanRequiredCode, body);
    }

    [Fact]
    public async Task TrialKey_AllowsSimulate_AndBlocksStartUntilActivated()
    {
        await using var app = BuildApp(enforceBilling: true);
        await app.StartAsync();
        using var client = app.GetTestClient();
        var tenant = await SeedTrialTenantAsync(app);

        var (simulateStatus, simulateBody) = await CallTool(
            client,
            tenant.TenantId,
            "simulate_workflowclass",
            new { workflowClassId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, simulateStatus);
        Assert.DoesNotContain(TenantEntitlementPolicy.PlanRequiredCode, simulateBody);

        var (startStatus, startBody) = await CallTool(
            client,
            tenant.TenantId,
            "start_workflow",
            new { workflowClassId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.PaymentRequired, startStatus);
        Assert.Contains(TenantEntitlementPolicy.PlanRequiredCode, startBody);
        Assert.Contains("MCP is included in the tenant subscription", startBody);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
            var stored = await db.Tenants.FindAsync(tenant.TenantId);
            Assert.NotNull(stored);
            stored.AssignPlan(TenantPlan.Managed, TenantBillingStatus.Active);
            await db.SaveChangesAsync();
        }

        var (paidStatus, paidBody) = await CallTool(
            client,
            tenant.TenantId,
            "start_workflow",
            new { workflowClassId = Guid.NewGuid() });
        Assert.NotEqual(HttpStatusCode.PaymentRequired, paidStatus);
        Assert.DoesNotContain(TenantEntitlementPolicy.PlanRequiredCode, paidBody);
    }

    private static Microsoft.AspNetCore.Builder.WebApplication BuildApp(bool enforceBilling) =>
        FlowOS.MCP.Program.BuildHttpApp([], builder =>
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
                ["FLOWOS_BILLING_ENFORCE"] = enforceBilling ? "true" : "false"
            });
        });

    private static async Task<Tenant> SeedTrialTenantAsync(Microsoft.AspNetCore.Builder.WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var tenant = new Tenant("MCP Trial " + Guid.NewGuid().ToString("N")[..8]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<(HttpStatusCode Status, string Body)> CallTool(
        HttpClient client,
        Guid tenantId,
        string toolName,
        object args)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonConvert.SerializeObject(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new { name = toolName, arguments = args }
            }), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-MCP-API-Key", ApiKey);
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        request.Headers.Add("MCP-Protocol-Version", McpJsonRpcDispatcher.SupportedProtocolVersion);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");

        var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
