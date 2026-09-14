using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services.Communication;
using FlowOS.Security.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.Services.Security;

public class TenantAuthService : ITenantAuthService
{
    private readonly FlowOSDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TenantAuthService> _logger;

    public const string OfficialFlowOsEmail = "admin@flowosbd.com";

    public TenantAuthService(
        FlowOSDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILogger<TenantAuthService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _emailSender = emailSender;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<RegisterTenantUserResult> RegisterTenantAsync(RegisterTenantUserRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TenantName))
            return new RegisterTenantUserResult(false, "Tenant name is required.");

        if (string.IsNullOrWhiteSpace(request.Email))
            return new RegisterTenantUserResult(false, "Email address is required.");

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            return new RegisterTenantUserResult(false, "Password must be at least 6 characters.");

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var trimmedTenantName = request.TenantName.Trim();

        var tenantExists = await _context.Tenants
            .AnyAsync(t => t.Name.ToLower() == trimmedTenantName.ToLower(), ct);
        if (tenantExists)
            return new RegisterTenantUserResult(false, $"A tenant with name '{trimmedTenantName}' already exists.");

        var userExists = await _context.TenantUsers
            .AnyAsync(u => u.Email == normalizedEmail, ct);
        if (userExists)
            return new RegisterTenantUserResult(false, $"A user with email '{normalizedEmail}' is already registered.");

        var tenant = new Tenant(trimmedTenantName, TenantStatus.PendingVerification);
        _context.Tenants.Add(tenant);

        var passwordHash = _passwordHasher.HashPassword(request.Password);
        var fullName = string.IsNullOrWhiteSpace(request.FullName) ? trimmedTenantName : request.FullName.Trim();

        var user = new TenantUser(
            tenant.TenantId,
            normalizedEmail,
            passwordHash,
            fullName,
            role: "Admin",
            isEmailVerified: false);

        var token = user.GenerateVerificationToken(TimeSpan.FromHours(24));
        _context.TenantUsers.Add(user);

        // Provision initial default API Key for the tenant
        var rawKey = TenantApiKey.GenerateRawKey();
        var apiKey = new TenantApiKey(
            tenant.TenantId,
            "Default API Key",
            rawKey,
            "FlowOS Default App",
            "Production",
            new List<string> { "*" });
        _context.TenantApiKeys.Add(apiKey);

        await _context.SaveChangesAsync(ct);

        // Dispatch verification email from official address
        await SendVerificationEmailInternalAsync(tenant, user, token, ct);

        var officialEmail = GetOfficialEmail();
        return new RegisterTenantUserResult(
            Success: true,
            Message: $"Tenant '{tenant.Name}' registered successfully. A verification email has been sent from {officialEmail} to {normalizedEmail}.",
            TenantId: tenant.TenantId,
            TenantName: tenant.Name,
            Email: user.Email,
            IsEmailVerified: false,
            VerificationToken: token);
    }

    public async Task<VerifyEmailResult> VerifyEmailAsync(string email, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return new VerifyEmailResult(false, "Email and verification token are required.");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _context.TenantUsers
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user == null)
            return new VerifyEmailResult(false, "User account not found.");

        if (user.IsEmailVerified)
            return new VerifyEmailResult(true, "Email address is already verified.", user.TenantId, user.Email, true);

        var verified = user.VerifyEmail(token);
        if (!verified)
        {
            if (user.EmailVerificationTokenExpiresAt.HasValue && user.EmailVerificationTokenExpiresAt.Value < DateTime.UtcNow)
            {
                return new VerifyEmailResult(false, "Verification token has expired. Please request a new verification email.");
            }
            return new VerifyEmailResult(false, "Invalid verification token.");
        }

        var tenant = await _context.Tenants.FindAsync(new object[] { user.TenantId }, ct);
        if (tenant != null && tenant.Status == TenantStatus.PendingVerification)
        {
            tenant.Activate();
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Tenant {TenantId} user {Email} verified email successfully.", user.TenantId, user.Email);

        return new VerifyEmailResult(true, "Email verified successfully. Your tenant account is now active.", user.TenantId, user.Email, true);
    }

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return new LoginResult(false, "Email and password are required.", ErrorCode: "INVALID_CREDENTIALS");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _context.TenantUsers
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user == null || !_passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            return new LoginResult(false, "Invalid email or password.", ErrorCode: "INVALID_CREDENTIALS");
        }

        var officialEmail = GetOfficialEmail();
        if (!user.IsEmailVerified)
        {
            return new LoginResult(
                Success: false,
                Message: $"Email address '{user.Email}' is not verified. Please check your inbox for instructions from {officialEmail}, or request a new verification email.",
                ErrorCode: "EMAIL_NOT_VERIFIED");
        }

        var tenant = await _context.Tenants.FindAsync(new object[] { user.TenantId }, ct);
        if (tenant == null)
        {
            return new LoginResult(false, "Associated tenant not found.", ErrorCode: "TENANT_NOT_FOUND");
        }

        if (tenant.Status == TenantStatus.Suspended || tenant.Status == TenantStatus.Archived)
        {
            return new LoginResult(false, $"Tenant account is {tenant.Status}.", ErrorCode: "TENANT_INACTIVE");
        }

        user.RecordLogin();
        await _context.SaveChangesAsync(ct);

        var token = _jwtTokenService.GenerateToken(
            user.Id,
            user.Email,
            user.FullName,
            tenant.TenantId,
            tenant.Name,
            user.Role);

        var userDto = new TenantUserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.Role,
            tenant.TenantId,
            tenant.Name,
            user.IsEmailVerified,
            user.CreatedAt,
            user.LastLoginAt);

        return new LoginResult(
            Success: true,
            Message: "Login successful.",
            Token: token,
            TokenType: "Bearer",
            ExpiresIn: 86400,
            User: userDto);
    }

    public async Task<ResendVerificationResult> ResendVerificationEmailAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new ResendVerificationResult(false, "Email address is required.");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _context.TenantUsers
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user == null)
        {
            return new ResendVerificationResult(false, "User account not found.");
        }

        if (user.IsEmailVerified)
        {
            return new ResendVerificationResult(true, "Email is already verified.");
        }

        var tenant = await _context.Tenants.FindAsync(new object[] { user.TenantId }, ct);
        if (tenant == null)
        {
            return new ResendVerificationResult(false, "Tenant not found.");
        }

        var token = user.GenerateVerificationToken(TimeSpan.FromHours(24));
        await _context.SaveChangesAsync(ct);

        await SendVerificationEmailInternalAsync(tenant, user, token, ct);

        var officialEmail = GetOfficialEmail();
        return new ResendVerificationResult(
            Success: true,
            Message: $"Verification email has been resent from {officialEmail} to {user.Email}.",
            VerificationToken: token);
    }

    public async Task<TenantUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _context.TenantUsers
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null)
            return null;

        var tenant = await _context.Tenants.FindAsync(new object[] { user.TenantId }, ct);
        var tenantName = tenant?.Name ?? string.Empty;

        return new TenantUserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.Role,
            user.TenantId,
            tenantName,
            user.IsEmailVerified,
            user.CreatedAt,
            user.LastLoginAt);
    }

    private string GetOfficialEmail() =>
        _configuration["FlowOS:Communications:Email:OfficialEmail"] ?? OfficialFlowOsEmail;

    private async Task SendVerificationEmailInternalAsync(Tenant tenant, TenantUser user, string token, CancellationToken ct)
    {
        var officialEmail = GetOfficialEmail();
        var baseUrl = _configuration["FlowOS:Communications:Email:VerificationBaseUrl"]
                      ?? "https://flowosbd.com/verify-email";
        var verificationLink = $"{baseUrl}?token={token}&email={Uri.EscapeDataString(user.Email)}";

        var subject = $"Verify your FlowOS Tenant Account - {tenant.Name}";

        var body = $"Welcome to FlowOS!\n\n" +
                   $"Thank you for registering your organisation '{tenant.Name}'.\n\n" +
                   $"Please verify your email address by opening the following link:\n{verificationLink}\n\n" +
                   $"Verification Token: {token}\n\n" +
                   $"This link and token will expire in 24 hours.\n\n" +
                   $"Best regards,\n" +
                   $"FlowOS Team ({officialEmail})";

        var htmlBody = $@"<!DOCTYPE html>
<html>
<head><meta charset=""utf-8""><title>Verify your FlowOS Account</title></head>
<body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; line-height: 1.6; color: #1e293b; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background: #0f172a; padding: 24px; text-align: center; border-radius: 8px 8px 0 0;"">
        <h1 style=""color: #38bdf8; margin: 0; font-size: 24px; letter-spacing: -0.5px;"">FlowOS</h1>
        <p style=""color: #94a3b8; margin: 6px 0 0 0; font-size: 13px;"">State-Machine-Governed Workflow Operating System</p>
    </div>
    <div style=""border: 1px solid #e2e8f0; border-top: none; padding: 28px; border-radius: 0 0 8px 8px; background: #ffffff;"">
        <h2 style=""color: #0f172a; margin-top: 0;"">Welcome to FlowOS, {user.FullName}!</h2>
        <p>Thank you for registering your organisation <strong>{tenant.Name}</strong> on FlowOS.</p>
        <p>To start creating workflows, managing state machines, and collaborating, please verify your email address:</p>
        <div style=""text-align: center; margin: 28px 0;"">
            <a href=""{verificationLink}"" style=""background-color: #2563eb; color: #ffffff; padding: 12px 28px; text-decoration: none; border-radius: 6px; font-weight: 600; display: inline-block; font-size: 15px;"">Verify Email Address</a>
        </div>
        <p style=""font-size: 14px; color: #475569;"">Or manually copy and paste your verification token:</p>
        <div style=""background: #f8fafc; border: 1px solid #e2e8f0; padding: 12px; border-radius: 6px; font-family: monospace; font-size: 15px; text-align: center; color: #0f172a; letter-spacing: 1px;"">
            {token}
        </div>
        <hr style=""border: none; border-top: 1px solid #e2e8f0; margin: 28px 0;"" />
        <p style=""color: #64748b; font-size: 12px; margin: 0;"">
            This verification link will expire in 24 hours.<br/>
            If you did not create this account, please ignore this email or reach us at <a href=""mailto:{officialEmail}"" style=""color: #2563eb;"">{officialEmail}</a>.
        </p>
    </div>
</body>
</html>";

        var headers = new Dictionary<string, string>
        {
            ["From"] = officialEmail,
            ["Sender"] = officialEmail,
            ["Reply-To"] = officialEmail
        };

        try
        {
            await _emailSender.SendEmailAsync(new EmailSendRequest(
                tenant.TenantId,
                user.Email,
                subject,
                body,
                htmlBody,
                Headers: headers), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification email to {Email}", user.Email);
        }
    }
}
