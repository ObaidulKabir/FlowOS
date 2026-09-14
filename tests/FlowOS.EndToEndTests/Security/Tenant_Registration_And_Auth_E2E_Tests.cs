using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowOS.EndToEndTests.Security;

public class Tenant_Registration_And_Auth_E2E_Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public Tenant_Registration_And_Auth_E2E_Tests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var contextDescriptors = services.Where(d => d.ServiceType == typeof(FlowOSDbContext)).ToList();
                foreach (var d in contextDescriptors) services.Remove(d);
                var optionsDescriptors = services.Where(d => d.ServiceType == typeof(DbContextOptions<FlowOSDbContext>)).ToList();
                foreach (var d in optionsDescriptors) services.Remove(d);

                var dbName = "FlowOS_E2E_Auth_" + Guid.NewGuid();
                services.AddScoped<FlowOSDbContext>(provider =>
                {
                    var options = new DbContextOptionsBuilder<FlowOSDbContext>()
                        .UseInMemoryDatabase(dbName)
                        .EnableSensitiveDataLogging()
                        .Options;
                    return new TestFlowOSDbContext(options);
                });
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Full_Tenant_Registration_Verification_And_Login_Lifecycle_Succeeds()
    {
        var tenantName = "Apex Financial Technologies";
        var email = $"admin_{Guid.NewGuid():N}@apexfintech.com";
        var password = "SecurePassword#2026";
        var fullName = "Apex Administrator";

        // 1. Register a new tenant organization
        var registerRequest = new RegisterTenantUserRequest(
            TenantName: tenantName,
            Email: email,
            Password: password,
            FullName: fullName);

        var regResponse = await _client.PostAsJsonAsync("/api/auth/register-tenant", registerRequest);
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);

        var regContent = await regResponse.Content.ReadAsStringAsync();
        using var regJson = JsonDocument.Parse(regContent);
        var root = regJson.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        var tenantIdStr = root.GetProperty("tenantId").GetString();
        Assert.NotNull(tenantIdStr);
        var tenantId = Guid.Parse(tenantIdStr);
        Assert.Equal(tenantName, root.GetProperty("tenantName").GetString());
        Assert.False(root.GetProperty("isEmailVerified").GetBoolean());

        var verificationToken = root.GetProperty("verificationToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(verificationToken));

        // 2. Attempt login BEFORE email verification -> Expect 403 Forbidden
        var preVerifyLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.Forbidden, preVerifyLogin.StatusCode);

        var preVerifyContent = await preVerifyLogin.Content.ReadAsStringAsync();
        using var preVerifyJson = JsonDocument.Parse(preVerifyContent);
        Assert.Equal("EMAIL_NOT_VERIFIED", preVerifyJson.RootElement.GetProperty("errorCode").GetString());

        // 3. Verify email with code
        var verifyResponse = await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, verificationToken!));
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var verifyContent = await verifyResponse.Content.ReadAsStringAsync();
        using var verifyJson = JsonDocument.Parse(verifyContent);
        Assert.True(verifyJson.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(verifyJson.RootElement.GetProperty("isEmailVerified").GetBoolean());

        // 4. Login AFTER email verification -> Expect 200 OK with JWT Bearer Token
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        using var loginJson = JsonDocument.Parse(loginContent);
        var loginRoot = loginJson.RootElement;

        Assert.True(loginRoot.GetProperty("ok").GetBoolean());
        var jwtToken = loginRoot.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(jwtToken));
        Assert.Equal("Bearer", loginRoot.GetProperty("tokenType").GetString());

        var userElement = loginRoot.GetProperty("user");
        Assert.Equal(email.ToLowerInvariant(), userElement.GetProperty("email").GetString());
        Assert.Equal(tenantId, Guid.Parse(userElement.GetProperty("tenantId").GetString()!));
        Assert.True(userElement.GetProperty("isEmailVerified").GetBoolean());

        // 5. Access authenticated endpoint /api/auth/me using the issued JWT Bearer Token
        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
        meRequest.Headers.Add("x-tenant-id", tenantId.ToString());

        var meResponse = await _client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var meContent = await meResponse.Content.ReadAsStringAsync();
        using var meJson = JsonDocument.Parse(meContent);
        var meUser = meJson.RootElement.GetProperty("user");
        Assert.Equal(email.ToLowerInvariant(), meUser.GetProperty("email").GetString());
        Assert.Equal("Admin", meUser.GetProperty("role").GetString());
        Assert.Equal(tenantId, Guid.Parse(meUser.GetProperty("tenantId").GetString()!));

        // 6. Access tenant workspace endpoint using the JWT Bearer Token
        var workflowsRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/workflow-classes?tenantId={tenantId}");
        workflowsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
        workflowsRequest.Headers.Add("x-tenant-id", tenantId.ToString());

        var workflowsResponse = await _client.SendAsync(workflowsRequest);
        Assert.Equal(HttpStatusCode.OK, workflowsResponse.StatusCode);
    }

    [Fact]
    public async Task Duplicate_Email_Registration_Returns_Conflict()
    {
        var email = $"conflict_{Guid.NewGuid():N}@tenant.com";

        var request1 = new RegisterTenantUserRequest(
            TenantName: "First Tenant",
            Email: email,
            Password: "Password123!");

        var res1 = await _client.PostAsJsonAsync("/api/auth/register-tenant", request1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        var request2 = new RegisterTenantUserRequest(
            TenantName: "Second Tenant",
            Email: email,
            Password: "DifferentPassword123!");

        var res2 = await _client.PostAsJsonAsync("/api/auth/register-tenant", request2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
    }
}
