using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services.Communication;
using FlowOS.Infrastructure.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowOS.UnitTests.Security;

public class TenantAuthTests : IDisposable
{
    private readonly FlowOSDbContext _context;
    private readonly Pbkdf2PasswordHasher _passwordHasher;
    private readonly JwtTokenService _jwtTokenService;
    private readonly TestEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly TenantAuthService _authService;

    public TenantAuthTests()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new FlowOSDbContext(options);
        _passwordHasher = new Pbkdf2PasswordHasher();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FlowOS:Communications:Email:OfficialEmail"] = "admin@flowosbd.com",
                ["FlowOS:Communications:Email:SenderName"] = "FlowOS Admin",
                ["FlowOS:Communications:Email:VerificationBaseUrl"] = "https://flowosbd.com/verify-email",
                ["FlowOS:Auth:JwtSecret"] = "FlowOS_Test_Secret_Key_At_Least_32_Chars_Long!",
                ["FlowOS:Auth:JwtIssuer"] = "FlowOS-Test",
                ["FlowOS:Auth:JwtAudience"] = "FlowOS-Test-Tenants",
                ["FlowOS:Auth:TokenExpirationHours"] = "24"
            })
            .Build();

        _jwtTokenService = new JwtTokenService(_configuration, NullLogger<JwtTokenService>.Instance);
        _emailSender = new TestEmailSender();

        _authService = new TenantAuthService(
            _context,
            _passwordHasher,
            _jwtTokenService,
            _emailSender,
            _configuration,
            NullLogger<TenantAuthService>.Instance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task RegisterTenant_ValidRequest_CreatesTenantAndSendsVerificationEmailFromOfficialAddress()
    {
        var request = new RegisterTenantUserRequest(
            TenantName: "TechCorp Global",
            Email: "admin@techcorp.com",
            Password: "SecurePassword123!",
            FullName: "TechCorp Lead");

        var result = await _authService.RegisterTenantAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.TenantId);
        Assert.Equal("TechCorp Global", result.TenantName);
        Assert.Equal("admin@techcorp.com", result.Email);
        Assert.False(result.IsEmailVerified);
        Assert.False(string.IsNullOrWhiteSpace(result.VerificationToken));
        Assert.Contains("admin@flowosbd.com", result.Message);

        // Verify DB State
        var tenant = await _context.Tenants.FindAsync(result.TenantId);
        Assert.NotNull(tenant);
        Assert.Equal(TenantStatus.PendingVerification, tenant.Status);

        var user = await _context.TenantUsers.FirstOrDefaultAsync(u => u.Email == "admin@techcorp.com");
        Assert.NotNull(user);
        Assert.False(user.IsEmailVerified);
        Assert.Equal("TechCorp Lead", user.FullName);
        Assert.Equal("Admin", user.Role);
        Assert.True(_passwordHasher.VerifyPassword("SecurePassword123!", user.PasswordHash));

        // Verify Email Sent via IEmailSender
        Assert.Single(_emailSender.SentEmails);
        var email = _emailSender.SentEmails.Single();
        Assert.Equal("admin@techcorp.com", email.To);
        Assert.Contains("Verify your FlowOS Tenant Account", email.Subject);
        Assert.NotNull(email.Headers);
        Assert.Equal("admin@flowosbd.com", email.Headers["From"]);
        Assert.Contains(result.VerificationToken, email.Body);
        Assert.Contains("https://flowosbd.com/verify-email", email.Body);
        Assert.Contains("admin@flowosbd.com", email.Body);
    }

    [Fact]
    public async Task RegisterTenant_DuplicateTenantName_ReturnsConflict()
    {
        var first = new RegisterTenantUserRequest("GlobalLogistics", "user1@globallogistics.com", "SecretPass1!");
        var res1 = await _authService.RegisterTenantAsync(first);
        Assert.True(res1.Success);

        var second = new RegisterTenantUserRequest("GLOBALLOGISTICS", "user2@globallogistics.com", "SecretPass2!");
        var res2 = await _authService.RegisterTenantAsync(second);

        Assert.False(res2.Success);
        Assert.Contains("already exists", res2.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterTenant_DuplicateEmail_ReturnsConflict()
    {
        var first = new RegisterTenantUserRequest("OrgOne", "shared@domain.com", "SecretPass1!");
        var res1 = await _authService.RegisterTenantAsync(first);
        Assert.True(res1.Success);

        var second = new RegisterTenantUserRequest("OrgTwo", "SHARED@domain.com", "SecretPass2!");
        var res2 = await _authService.RegisterTenantAsync(second);

        Assert.False(res2.Success);
        Assert.Contains("already registered", res2.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyEmail_ValidToken_ActivatesTenantAndVerifiesUser()
    {
        var request = new RegisterTenantUserRequest("AlphaSoft", "contact@alphasoft.com", "StrongPassword1!");
        var regResult = await _authService.RegisterTenantAsync(request);

        var verifyResult = await _authService.VerifyEmailAsync(regResult.Email!, regResult.VerificationToken!);

        Assert.True(verifyResult.Success);
        Assert.True(verifyResult.IsEmailVerified);
        Assert.Equal(regResult.TenantId, verifyResult.TenantId);

        // Verify DB State
        var user = await _context.TenantUsers.FirstOrDefaultAsync(u => u.Email == "contact@alphasoft.com");
        Assert.NotNull(user);
        Assert.True(user.IsEmailVerified);
        Assert.NotNull(user.EmailVerifiedAt);
        Assert.Null(user.EmailVerificationToken);

        var tenant = await _context.Tenants.FindAsync(regResult.TenantId);
        Assert.NotNull(tenant);
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Fact]
    public async Task VerifyEmail_InvalidToken_ReturnsFailure()
    {
        var request = new RegisterTenantUserRequest("BetaCorp", "admin@betacorp.com", "StrongPassword1!");
        var regResult = await _authService.RegisterTenantAsync(request);

        var verifyResult = await _authService.VerifyEmailAsync(regResult.Email!, "invalid-hex-token");

        Assert.False(verifyResult.Success);
        Assert.Contains("Invalid verification token", verifyResult.Message);

        var user = await _context.TenantUsers.FirstOrDefaultAsync(u => u.Email == "admin@betacorp.com");
        Assert.NotNull(user);
        Assert.False(user.IsEmailVerified);
    }

    [Fact]
    public async Task VerifyEmail_ExpiredToken_ReturnsFailure()
    {
        var request = new RegisterTenantUserRequest("GammaLtd", "admin@gammaltd.com", "StrongPassword1!");
        var regResult = await _authService.RegisterTenantAsync(request);

        // Expire token manually
        var user = await _context.TenantUsers.FirstOrDefaultAsync(u => u.Email == "admin@gammaltd.com");
        Assert.NotNull(user);
        user.GenerateVerificationToken(TimeSpan.FromMinutes(-5));
        await _context.SaveChangesAsync();

        var verifyResult = await _authService.VerifyEmailAsync("admin@gammaltd.com", user.EmailVerificationToken!);

        Assert.False(verifyResult.Success);
        Assert.Contains("expired", verifyResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_UnverifiedEmail_ReturnsEmailNotVerifiedErrorWithOfficialContact()
    {
        var request = new RegisterTenantUserRequest("DeltaEnterprises", "admin@delta.com", "Secret1234!");
        await _authService.RegisterTenantAsync(request);

        var loginResult = await _authService.LoginAsync("admin@delta.com", "Secret1234!");

        Assert.False(loginResult.Success);
        Assert.Equal("EMAIL_NOT_VERIFIED", loginResult.ErrorCode);
        Assert.Contains("admin@flowosbd.com", loginResult.Message);
        Assert.Null(loginResult.Token);
    }

    [Fact]
    public async Task Login_VerifiedEmail_ReturnsValidJwtTokenAndUserDetails()
    {
        var request = new RegisterTenantUserRequest("EpsilonTech", "ceo@epsilon.com", "EpsilonPass123!", "Epsilon CEO");
        var regResult = await _authService.RegisterTenantAsync(request);
        await _authService.VerifyEmailAsync(regResult.Email!, regResult.VerificationToken!);

        var loginResult = await _authService.LoginAsync("ceo@epsilon.com", "EpsilonPass123!");

        Assert.True(loginResult.Success);
        Assert.NotNull(loginResult.Token);
        Assert.Equal("Bearer", loginResult.TokenType);
        Assert.NotNull(loginResult.User);
        Assert.Equal("ceo@epsilon.com", loginResult.User.Email);
        Assert.Equal("Epsilon CEO", loginResult.User.FullName);
        Assert.Equal("EpsilonTech", loginResult.User.TenantName);
        Assert.Equal("Admin", loginResult.User.Role);
        Assert.True(loginResult.User.IsEmailVerified);

        // Verify JWT validation with ClaimsPrincipal
        var principal = _jwtTokenService.ValidateToken(loginResult.Token!);
        Assert.NotNull(principal);
        Assert.Equal(loginResult.User.Id.ToString(), principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("ceo@epsilon.com", principal.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal(regResult.TenantId.ToString(), principal.FindFirst("tenant_id")?.Value);
        Assert.Equal("EpsilonTech", principal.FindFirst("tenant_name")?.Value);
        Assert.Equal("Admin", principal.FindFirst(ClaimTypes.Role)?.Value);
    }

    [Fact]
    public async Task Login_InvalidPassword_ReturnsInvalidCredentials()
    {
        var request = new RegisterTenantUserRequest("ZetaLLC", "lead@zeta.com", "CorrectPassword1!");
        var regResult = await _authService.RegisterTenantAsync(request);
        await _authService.VerifyEmailAsync(regResult.Email!, regResult.VerificationToken!);

        var loginResult = await _authService.LoginAsync("lead@zeta.com", "WrongPassword999!");

        Assert.False(loginResult.Success);
        Assert.Equal("INVALID_CREDENTIALS", loginResult.ErrorCode);
        Assert.Null(loginResult.Token);
    }

    [Fact]
    public async Task ResendVerification_SendsNewTokenFromOfficialAddress()
    {
        var request = new RegisterTenantUserRequest("EtaNetwork", "ops@etanetwork.com", "Pass123456!");
        await _authService.RegisterTenantAsync(request);
        _emailSender.SentEmails.Clear();

        var resendResult = await _authService.ResendVerificationEmailAsync("ops@etanetwork.com");

        Assert.True(resendResult.Success);
        Assert.NotNull(resendResult.VerificationToken);

        Assert.Single(_emailSender.SentEmails);
        var email = _emailSender.SentEmails.Single();
        Assert.Equal("ops@etanetwork.com", email.To);
        Assert.Equal("admin@flowosbd.com", email.Headers!["From"]);
        Assert.Contains(resendResult.VerificationToken, email.Body);

        // Verify that the new token works for verification
        var verifyResult = await _authService.VerifyEmailAsync("ops@etanetwork.com", resendResult.VerificationToken);
        Assert.True(verifyResult.Success);
        Assert.True(verifyResult.IsEmailVerified);
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsCorrectProfile()
    {
        var request = new RegisterTenantUserRequest("ThetaDev", "dev@thetadev.com", "Password123!", "Theta Developer");
        var regResult = await _authService.RegisterTenantAsync(request);
        await _authService.VerifyEmailAsync(regResult.Email!, regResult.VerificationToken!);

        var user = await _context.TenantUsers.FirstOrDefaultAsync(u => u.Email == "dev@thetadev.com");
        Assert.NotNull(user);

        var profile = await _authService.GetCurrentUserAsync(user.Id);

        Assert.NotNull(profile);
        Assert.Equal(user.Id, profile.Id);
        Assert.Equal("dev@thetadev.com", profile.Email);
        Assert.Equal("Theta Developer", profile.FullName);
        Assert.Equal("ThetaDev", profile.TenantName);
        Assert.True(profile.IsEmailVerified);
    }

    private class TestEmailSender : IEmailSender
    {
        public List<EmailSendRequest> SentEmails { get; } = new();

        public Task<EmailSendResult> SendEmailAsync(EmailSendRequest request, CancellationToken ct = default)
        {
            SentEmails.Add(request);
            return Task.FromResult(new EmailSendResult(true, $"test_{Guid.NewGuid():N}", 200));
        }
    }
}
