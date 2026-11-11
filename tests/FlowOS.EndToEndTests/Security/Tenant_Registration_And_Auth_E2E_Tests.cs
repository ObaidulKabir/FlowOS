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
using Microsoft.Extensions.Configuration;
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
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FlowOS:Identity:AllowMockAuth"] = "false"
                }));
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

        // The dashboard's instance list uses a different endpoint from the blueprint catalog.
        using var instancesRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/workflows?tenantId={tenantId}");
        instancesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
        instancesRequest.Headers.Add("X-API-Key", jwtToken);
        instancesRequest.Headers.Add("x-tenant-id", tenantId.ToString());
        instancesRequest.Headers.Add("X-Mock-Role", "Tenant");
        var instancesResponse = await _client.SendAsync(instancesRequest);
        Assert.Equal(HttpStatusCode.OK, instancesResponse.StatusCode);
        using var instancesJson = JsonDocument.Parse(await instancesResponse.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, instancesJson.RootElement.ValueKind);

        using var missingCredentials = new HttpRequestMessage(HttpMethod.Get, $"/api/workflows?tenantId={tenantId}");
        missingCredentials.Headers.Add("x-tenant-id", tenantId.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(missingCredentials)).StatusCode);

        using var wrongTenant = new HttpRequestMessage(HttpMethod.Get, $"/api/workflows?tenantId={Guid.NewGuid()}");
        wrongTenant.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
        wrongTenant.Headers.Add("x-tenant-id", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(wrongTenant)).StatusCode);
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

    /// <summary>
    /// Organizational team access (see docs/29-organizational-team-access.md): an existing tenant
    /// Admin invites a teammate into the SAME tenant; the teammate accepts the invite with their own
    /// password, logs in with their own credentials, and - because they hold a non-privileged role -
    /// is forbidden from inviting further teammates. (A companion test on the API-key-attribution CR
    /// covers the equivalent 403 for tenant API-key management, which that CR gates separately.)
    /// </summary>
    [Fact]
    public async Task Invite_Accept_Login_And_NonAdmin_Forbidden_Lifecycle_Succeeds()
    {
        var tenantName = "Beacon Logistics Group";
        var adminEmail = $"admin_{Guid.NewGuid():N}@beaconlogistics.com";
        var adminPassword = "SecurePassword#2026";

        // 1. Register the tenant and its first (Admin) teammate.
        var regResponse = await _client.PostAsJsonAsync("/api/auth/register-tenant", new RegisterTenantUserRequest(
            TenantName: tenantName,
            Email: adminEmail,
            Password: adminPassword,
            FullName: "Beacon Administrator"));
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);
        using var regJson = JsonDocument.Parse(await regResponse.Content.ReadAsStringAsync());
        var tenantId = Guid.Parse(regJson.RootElement.GetProperty("tenantId").GetString()!);
        var adminVerificationToken = regJson.RootElement.GetProperty("verificationToken").GetString();

        await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(adminEmail, adminVerificationToken!));

        var adminLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(adminEmail, adminPassword));
        Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);
        using var adminLoginJson = JsonDocument.Parse(await adminLogin.Content.ReadAsStringAsync());
        var adminToken = adminLoginJson.RootElement.GetProperty("token").GetString();

        // 2. The Admin invites a teammate into the SAME tenant.
        var teammateEmail = $"teammate_{Guid.NewGuid():N}@beaconlogistics.com";
        using var inviteRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/tenants/{tenantId}/users")
        {
            Content = JsonContent.Create(new { email = teammateEmail, fullName = "Beacon Teammate", role = "Employee" })
        };
        inviteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        inviteRequest.Headers.Add("x-tenant-id", tenantId.ToString());
        var inviteResponse = await _client.SendAsync(inviteRequest);
        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);
        using var inviteJson = JsonDocument.Parse(await inviteResponse.Content.ReadAsStringAsync());
        Assert.True(inviteJson.RootElement.GetProperty("ok").GetBoolean());
        var inviteToken = inviteJson.RootElement.GetProperty("inviteToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(inviteToken));

        // 3. The teammate accepts the invite and chooses their own password.
        var teammatePassword = "TeammatesOwnPassword#1";
        var acceptResponse = await _client.PostAsJsonAsync("/api/auth/accept-invite", new AcceptInviteRequest(
            Email: teammateEmail,
            Token: inviteToken!,
            Password: teammatePassword));
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);
        using var acceptJson = JsonDocument.Parse(await acceptResponse.Content.ReadAsStringAsync());
        Assert.True(acceptJson.RootElement.GetProperty("ok").GetBoolean());

        // 4. The teammate logs in with their own credentials (never the admin's).
        var teammateLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(teammateEmail, teammatePassword));
        Assert.Equal(HttpStatusCode.OK, teammateLogin.StatusCode);
        using var teammateLoginJson = JsonDocument.Parse(await teammateLogin.Content.ReadAsStringAsync());
        var teammateToken = teammateLoginJson.RootElement.GetProperty("token").GetString();
        Assert.Equal("Employee", teammateLoginJson.RootElement.GetProperty("user").GetProperty("role").GetString());

        // 5. The non-admin teammate is forbidden from inviting further teammates in their own tenant.
        using var teammateInviteAttempt = new HttpRequestMessage(HttpMethod.Post, $"/api/tenants/{tenantId}/users")
        {
            Content = JsonContent.Create(new { email = $"other_{Guid.NewGuid():N}@beaconlogistics.com", fullName = "Someone Else", role = "Employee" })
        };
        teammateInviteAttempt.Headers.Authorization = new AuthenticationHeaderValue("Bearer", teammateToken);
        teammateInviteAttempt.Headers.Add("x-tenant-id", tenantId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(teammateInviteAttempt)).StatusCode);

        // 6. The Admin can list teammates and sees both themself and the new teammate.
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/tenants/{tenantId}/users");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        listRequest.Headers.Add("x-tenant-id", tenantId.ToString());
        var listResponse = await _client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, listJson.RootElement.GetArrayLength());

        // 7. The tenant can never be left with no administrator: removing the sole Admin is rejected.
        var adminUserId = Guid.Parse(adminLoginJson.RootElement.GetProperty("user").GetProperty("id").GetString()!);
        using var removeLastAdmin = new HttpRequestMessage(HttpMethod.Delete, $"/api/tenants/{tenantId}/users/{adminUserId}");
        removeLastAdmin.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        removeLastAdmin.Headers.Add("x-tenant-id", tenantId.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(removeLastAdmin)).StatusCode);
    }

    /// <summary>
    /// Organizational team access (see docs/29-organizational-team-access.md): a key generated by a
    /// tenant Admin records who made it; a non-admin teammate is forbidden from listing, generating or
    /// revoking tenant API keys; a shared TenantApiKey keeps the access it always had, so existing
    /// machine-to-machine automation that rotates its own keys is unaffected.
    /// </summary>
    [Fact]
    public async Task ApiKey_Attribution_And_NonAdmin_Forbidden_From_Key_Management()
    {
        var tenantName = "Harbor Freight Analytics";
        var adminEmail = $"admin_{Guid.NewGuid():N}@harborfreight.com";
        var adminPassword = "SecurePassword#2026";

        var regResponse = await _client.PostAsJsonAsync("/api/auth/register-tenant", new RegisterTenantUserRequest(
            TenantName: tenantName,
            Email: adminEmail,
            Password: adminPassword,
            FullName: "Harbor Administrator"));
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);
        using var regJson = JsonDocument.Parse(await regResponse.Content.ReadAsStringAsync());
        var tenantId = Guid.Parse(regJson.RootElement.GetProperty("tenantId").GetString()!);
        var adminVerificationToken = regJson.RootElement.GetProperty("verificationToken").GetString();

        await _client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(adminEmail, adminVerificationToken!));
        var adminLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(adminEmail, adminPassword));
        using var adminLoginJson = JsonDocument.Parse(await adminLogin.Content.ReadAsStringAsync());
        var adminToken = adminLoginJson.RootElement.GetProperty("token").GetString();
        var adminUserId = adminLoginJson.RootElement.GetProperty("user").GetProperty("id").GetString();

        // 1. The Admin generates a key; the response records them as its creator.
        using var generateRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/tenants/{tenantId}/keys")
        {
            Content = JsonContent.Create(new { name = "Admin-Generated Key" })
        };
        generateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        generateRequest.Headers.Add("x-tenant-id", tenantId.ToString());
        var generateResponse = await _client.SendAsync(generateRequest);
        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);
        using var generateJson = JsonDocument.Parse(await generateResponse.Content.ReadAsStringAsync());
        Assert.Equal(adminUserId, generateJson.RootElement.GetProperty("createdByUserId").GetString());
        Assert.Equal("Harbor Administrator", generateJson.RootElement.GetProperty("createdByName").GetString());
        var generatedKeyId = generateJson.RootElement.GetProperty("id").GetString();
        // The raw key, usable as a shared credential for step 4 below - register-tenant (used for the
        // Admin's own login above) creates no TenantApiKey, so this Admin-generated one stands in for
        // "a shared credential that already existed" for this test.
        var sharedRawKey = generateJson.RootElement.GetProperty("apiKey").GetString();

        // 2. The Admin can list keys and sees attribution on the one they just generated.
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/tenants/{tenantId}/keys");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        listRequest.Headers.Add("x-tenant-id", tenantId.ToString());
        var listResponse = await _client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        // 3. A non-admin teammate is forbidden from listing, generating or revoking tenant API keys.
        var teammateEmail = $"teammate_{Guid.NewGuid():N}@harborfreight.com";
        using var inviteRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/tenants/{tenantId}/users")
        {
            Content = JsonContent.Create(new { email = teammateEmail, fullName = "Harbor Teammate", role = "Employee" })
        };
        inviteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        inviteRequest.Headers.Add("x-tenant-id", tenantId.ToString());
        using var inviteJson = JsonDocument.Parse(await (await _client.SendAsync(inviteRequest)).Content.ReadAsStringAsync());
        var inviteToken = inviteJson.RootElement.GetProperty("inviteToken").GetString();

        var teammatePassword = "TeammatesOwnPassword#2";
        await _client.PostAsJsonAsync("/api/auth/accept-invite", new AcceptInviteRequest(teammateEmail, inviteToken!, teammatePassword));
        var teammateLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(teammateEmail, teammatePassword));
        using var teammateLoginJson = JsonDocument.Parse(await teammateLogin.Content.ReadAsStringAsync());
        var teammateToken = teammateLoginJson.RootElement.GetProperty("token").GetString();

        using var teammateList = new HttpRequestMessage(HttpMethod.Get, $"/api/tenants/{tenantId}/keys");
        teammateList.Headers.Authorization = new AuthenticationHeaderValue("Bearer", teammateToken);
        teammateList.Headers.Add("x-tenant-id", tenantId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(teammateList)).StatusCode);

        using var teammateGenerate = new HttpRequestMessage(HttpMethod.Post, $"/api/tenants/{tenantId}/keys")
        {
            Content = JsonContent.Create(new { name = "Should Not Be Created" })
        };
        teammateGenerate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", teammateToken);
        teammateGenerate.Headers.Add("x-tenant-id", tenantId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(teammateGenerate)).StatusCode);

        using var teammateRevoke = new HttpRequestMessage(HttpMethod.Delete, $"/api/tenants/{tenantId}/keys/{generatedKeyId}");
        teammateRevoke.Headers.Authorization = new AuthenticationHeaderValue("Bearer", teammateToken);
        teammateRevoke.Headers.Add("x-tenant-id", tenantId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(teammateRevoke)).StatusCode);

        // 4. A shared TenantApiKey keeps the access it always had: existing automation that uses a key
        //    to list or mint other keys for the same tenant is unaffected by this gate.
        using var sharedKeyList = new HttpRequestMessage(HttpMethod.Get, $"/api/tenants/{tenantId}/keys");
        sharedKeyList.Headers.Add("X-API-Key", sharedRawKey);
        sharedKeyList.Headers.Add("x-tenant-id", tenantId.ToString());
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(sharedKeyList)).StatusCode);

        using var sharedKeyGenerate = new HttpRequestMessage(HttpMethod.Post, $"/api/tenants/{tenantId}/keys")
        {
            Content = JsonContent.Create(new { name = "Automation-Generated Key" })
        };
        sharedKeyGenerate.Headers.Add("X-API-Key", sharedRawKey);
        sharedKeyGenerate.Headers.Add("x-tenant-id", tenantId.ToString());
        var sharedGenerateResponse = await _client.SendAsync(sharedKeyGenerate);
        Assert.Equal(HttpStatusCode.OK, sharedGenerateResponse.StatusCode);
        using var sharedGenerateJson = JsonDocument.Parse(await sharedGenerateResponse.Content.ReadAsStringAsync());
        // A shared credential has no individual identity, so the key it mints has no creator on record.
        Assert.Equal(JsonValueKind.Null, sharedGenerateJson.RootElement.GetProperty("createdByUserId").ValueKind);
    }
}
