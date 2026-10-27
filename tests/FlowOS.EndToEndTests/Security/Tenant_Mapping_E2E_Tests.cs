using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Security;

/// <summary>
/// CR-14: Learning OS (acting as a platform administrator) provisions one FlowOS tenant per institution,
/// keyed by its own reference, finds it again, and operates inside it with that tenant's scoped key.
/// </summary>
public class Tenant_Mapping_E2E_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid PlatformTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _ref = "learning-os:board-dhaka/school-" + Guid.NewGuid().ToString("N")[..8];

    public Tenant_Mapping_E2E_Tests(WebApplicationFactory<Program> factory)
    {
        var dbName = "FlowOS_E2E_TenantMapping_" + Guid.NewGuid();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList()) services.Remove(d);
                foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList()) services.Remove(d);
                services.AddScoped<FlowOSDbContext>(_ => new TestFlowOSDbContext(
                    new DbContextOptionsBuilder<FlowOSDbContext>().UseInMemoryDatabase(dbName).Options));
            });
        });
    }

    private HttpClient As(Guid tenant, string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-tenant-id", tenant.ToString());
        client.DefaultRequestHeaders.Add("X-Mock-Role", role);
        return client;
    }

    private async Task<(Guid TenantId, string ApiKey)> ProvisionAsync(HttpClient platform, string name)
    {
        var response = await platform.PostAsJsonAsync("/api/tenants", new { name, externalTenantRef = _ref, keyName = "Learning OS service" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tenant = json.RootElement.GetProperty("tenant");
        Assert.Equal(_ref, tenant.GetProperty("externalTenantRef").GetString());
        return (tenant.GetProperty("tenantId").GetGuid(), json.RootElement.GetProperty("apiKey").GetString()!);
    }

    [Fact]
    public async Task Provisioning_by_reference_is_idempotent_and_findable()
    {
        var platform = As(PlatformTenant);
        var (tenantId, _) = await ProvisionAsync(platform, "Dhaka School " + Guid.NewGuid().ToString("N")[..6]);

        var retry = await platform.PostAsJsonAsync("/api/tenants", new { name = "Retry with another name", externalTenantRef = _ref });
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        using (var json = JsonDocument.Parse(await retry.Content.ReadAsStringAsync()))
        {
            Assert.Equal("TENANT-EXISTS", json.RootElement.GetProperty("code").GetString());
            Assert.Equal(tenantId, json.RootElement.GetProperty("tenantId").GetGuid());
        }

        var lookup = await platform.GetAsync("/api/tenants?externalRef=" + Uri.EscapeDataString(_ref));
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        using (var json = JsonDocument.Parse(await lookup.Content.ReadAsStringAsync()))
        {
            var only = Assert.Single(json.RootElement.EnumerateArray());
            Assert.Equal(tenantId, only.GetProperty("tenantId").GetGuid());
        }
    }

    [Fact]
    public async Task The_tenant_key_works_only_inside_its_tenant()
    {
        var (tenantId, apiKey) = await ProvisionAsync(As(PlatformTenant), "Isolation School " + Guid.NewGuid().ToString("N")[..6]);

        HttpRequestMessage WithKey(Guid headerTenant)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/workflows");
            request.Headers.Add("X-API-Key", apiKey);
            request.Headers.Add("x-tenant-id", headerTenant.ToString());
            return request;
        }

        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(WithKey(tenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(WithKey(OtherTenant))).StatusCode);
    }

    [Fact]
    public async Task Only_platform_administrators_can_provision_or_look_up_tenants()
    {
        var tenantAdmin = As(OtherTenant);

        Assert.Equal(HttpStatusCode.Forbidden, (await tenantAdmin.PostAsJsonAsync("/api/tenants", new { name = "Sneaky", externalTenantRef = _ref })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenantAdmin.GetAsync("/api/tenants?externalRef=" + Uri.EscapeDataString(_ref))).StatusCode);
    }

    [Fact]
    public async Task Reference_can_be_set_later_cleared_and_is_unique()
    {
        var platform = As(PlatformTenant);
        var first = await platform.PostAsJsonAsync("/api/tenants", new { name = "Later Mapped " + Guid.NewGuid().ToString("N")[..6] });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Guid firstId;
        using (var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
            firstId = json.RootElement.GetProperty("tenant").GetProperty("tenantId").GetGuid();
        var (mappedId, _) = await ProvisionAsync(platform, "Already Mapped " + Guid.NewGuid().ToString("N")[..6]);

        var duplicate = await platform.PutAsJsonAsync($"/api/tenants/{firstId}/external-ref", new { externalTenantRef = _ref });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var set = await platform.PutAsJsonAsync($"/api/tenants/{firstId}/external-ref", new { externalTenantRef = _ref + "-annex" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var cleared = await platform.PutAsJsonAsync($"/api/tenants/{mappedId}/external-ref", new { externalTenantRef = (string?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        var lookup = await platform.GetAsync("/api/tenants?externalRef=" + Uri.EscapeDataString(_ref));
        using var result = JsonDocument.Parse(await lookup.Content.ReadAsStringAsync());
        Assert.Empty(result.RootElement.EnumerateArray());

        var tooLong = await platform.PutAsJsonAsync($"/api/tenants/{firstId}/external-ref", new { externalTenantRef = new string('x', 201) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await platform.PutAsJsonAsync($"/api/tenants/{Guid.NewGuid()}/external-ref", new { externalTenantRef = "x" })).StatusCode);
    }
}
