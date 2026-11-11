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
    private readonly TenantSecurityProvisioningService _securityProvisioning;

    public const string OfficialFlowOsEmail = "admin@flowosbd.com";

    private readonly TimeProvider _timeProvider;

    public TenantAuthService(
        FlowOSDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILogger<TenantAuthService> logger,
        TenantSecurityProvisioningService securityProvisioning,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _emailSender = emailSender;
        _configuration = configuration;
        _logger = logger;
        _securityProvisioning = securityProvisioning;
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
        await _securityProvisioning.EnsureTenantAsync(tenant.TenantId, ct);

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
            if (user.EmailVerificationTokenExpiresAt.HasValue && user.EmailVerificationTokenExpiresAt.Value < _timeProvider.GetUtcNow().UtcDateTime)
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

    public async Task<VerifyEmailResult> VerifyEmailWithPasswordAsync(string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return new VerifyEmailResult(false, "Email and password are required.");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _context.TenantUsers
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user == null || !_passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            return new VerifyEmailResult(false, "Invalid email or password.");
        }

        if (user.IsEmailVerified)
        {
            return new VerifyEmailResult(true, "Email address is already verified.", user.TenantId, user.Email, true);
        }

        user.MarkEmailAsVerified();

        var tenant = await _context.Tenants.FindAsync(new object[] { user.TenantId }, ct);
        if (tenant != null && tenant.Status == TenantStatus.PendingVerification)
        {
            tenant.Activate();
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Tenant {TenantId} user {Email} verified email via password credentials.", user.TenantId, user.Email);

        return new VerifyEmailResult(true, "Email verified successfully with your account credentials. Your tenant account is now active.", user.TenantId, user.Email, true);
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

        var assignedRoles = await _context.TenantUserRoles
            .AsNoTracking()
            .Where(a => a.TenantId == user.TenantId && a.TenantUserId == user.Id)
            .Join(
                _context.Roles.AsNoTracking().Where(r => r.TenantId == user.TenantId),
                assignment => assignment.RoleId,
                role => role.Id,
                (_, role) => role.Name)
            .ToListAsync(ct);

        var token = _jwtTokenService.GenerateToken(
            user.Id,
            user.Email,
            user.FullName,
            tenant.TenantId,
            tenant.Name,
            user.Role,
            assignedRoles);

        var userDto = new TenantUserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.Role,
            tenant.TenantId,
            tenant.Name,
            user.IsEmailVerified,
            user.CreatedAt,
            user.LastLoginAt,
            tenant.Plan.ToString(),
            tenant.BillingStatus.ToString(),
            tenant.CanRunRuntime);

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
            user.LastLoginAt,
            tenant?.Plan.ToString() ?? TenantPlan.Trial.ToString(),
            tenant?.BillingStatus.ToString() ?? TenantBillingStatus.Unpaid.ToString(),
            tenant?.CanRunRuntime ?? false);
    }

    public async Task<InviteTenantUserResult> InviteUserAsync(InviteTenantUserRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return new InviteTenantUserResult(false, "Email address is required.");

        var role = string.IsNullOrWhiteSpace(request.Role) ? "Employee" : request.Role.Trim();
        var fullName = string.IsNullOrWhiteSpace(request.FullName) ? request.Email.Trim() : request.FullName.Trim();

        var tenant = await _context.Tenants.FindAsync(new object[] { request.TenantId }, ct);
        if (tenant == null)
            return new InviteTenantUserResult(false, "Tenant not found.");
        if (tenant.Status == TenantStatus.Suspended || tenant.Status == TenantStatus.Archived)
            return new InviteTenantUserResult(false, $"Tenant account is {tenant.Status}.");

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userExists = await _context.TenantUsers.AnyAsync(u => u.Email == normalizedEmail, ct);
        if (userExists)
            return new InviteTenantUserResult(false, $"A user with email '{normalizedEmail}' is already registered.");

        // The teammate sets their own password when they accept the invite (AcceptInviteAsync) -
        // this placeholder is a random secret nobody is ever told, so the account can't be logged
        // into with it, only with the real password the teammate later chooses.
        var placeholderPasswordHash = _passwordHasher.HashPassword(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));

        var user = new TenantUser(
            tenant.TenantId,
            normalizedEmail,
            placeholderPasswordHash,
            fullName,
            role: role,
            isEmailVerified: false);
        _context.TenantUsers.Add(user);

        await _context.SaveChangesAsync(ct);
        await SendInviteEmailInternalAsync(tenant, user, user.EmailVerificationToken!, ct);

        return new InviteTenantUserResult(
            Success: true,
            Message: $"Invited {normalizedEmail} to {tenant.Name} as {role}. An invite email has been sent from {GetOfficialEmail()}.",
            UserId: user.Id,
            Email: user.Email,
            InviteToken: user.EmailVerificationToken);
    }

    public async Task<AcceptInviteResult> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Token))
            return new AcceptInviteResult(false, "Email and invite code are required.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            return new AcceptInviteResult(false, "Password must be at least 6 characters.");

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _context.TenantUsers.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        if (user == null)
            return new AcceptInviteResult(false, "Invite not found for this email address.");

        if (user.IsEmailVerified)
            return new AcceptInviteResult(false, "This invite was already accepted. Try logging in instead.");

        var accepted = user.VerifyEmail(request.Token);
        if (!accepted)
        {
            if (user.EmailVerificationTokenExpiresAt.HasValue &&
                user.EmailVerificationTokenExpiresAt.Value < _timeProvider.GetUtcNow().UtcDateTime)
            {
                return new AcceptInviteResult(false, "Invite code has expired. Ask an admin to invite you again.");
            }
            return new AcceptInviteResult(false, "Invalid invite code.");
        }

        user.UpdatePassword(_passwordHasher.HashPassword(request.Password));
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Tenant {TenantId} teammate {Email} accepted their invite.", user.TenantId, user.Email);

        return new AcceptInviteResult(true, "Invite accepted. You can now log in.", user.TenantId, user.Email);
    }

    public async Task<IReadOnlyList<TenantTeammateDto>> ListTeammatesAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _context.TenantUsers
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new TenantTeammateDto(u.Id, u.Email, u.FullName, u.Role, u.IsEmailVerified, u.CreatedAt, u.LastLoginAt))
            .ToListAsync(ct);
    }

    public async Task<TeammateActionResult> RemoveTeammateAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var teammates = await _context.TenantUsers
            .Where(u => u.TenantId == tenantId)
            .ToListAsync(ct);

        var target = teammates.FirstOrDefault(u => u.Id == userId);
        if (target == null)
            return new TeammateActionResult(false, "Teammate not found in this tenant.");

        // A tenant must always keep at least one administrator able to manage it. This only looks at
        // each teammate's primary role (not any additional custom roles assigned via Runtime RBAC),
        // which is the same signal used to grant the very first Admin at tenant registration.
        var remainingAdmins = teammates.Count(u => u.Id != userId && FlowOS.Core.Security.TenantIdentityRules.IsPrivilegedRole(u.Role));
        if (remainingAdmins == 0 && FlowOS.Core.Security.TenantIdentityRules.IsPrivilegedRole(target.Role))
        {
            return new TeammateActionResult(false, "Cannot remove the tenant's last administrator. Promote another teammate to Admin first.");
        }

        _context.TenantUsers.Remove(target);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Tenant {TenantId} removed teammate {Email}.", tenantId, target.Email);

        return new TeammateActionResult(true, $"Removed {target.Email} from the tenant.");
    }

    private string GetOfficialEmail() =>
        _configuration["FlowOS:Communications:Email:OfficialEmail"] ?? OfficialFlowOsEmail;

    private async Task SendInviteEmailInternalAsync(Tenant tenant, TenantUser user, string token, CancellationToken ct)
    {
        var officialEmail = GetOfficialEmail();
        var subject = $"You've been invited to {tenant.Name} on FlowOS (Code: {token})";

        var body = $"You've been invited to join '{tenant.Name}' on FlowOS as {user.Role}.\n\n" +
                   $"Your 6-Digit Invite Code: {token}\n\n" +
                   $"Open the FlowOS dashboard, choose \"Accept Invite\", and enter this code with the email " +
                   $"{user.Email} to set your own password and sign in.\n\n" +
                   $"This code will expire in 24 hours.\n\n" +
                   $"Best regards,\n" +
                   $"FlowOS Team ({officialEmail})";

        var htmlBody = $@"<!DOCTYPE html>
<html>
<head><meta charset=""utf-8""><title>You're invited to FlowOS</title></head>
<body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; line-height: 1.6; color: #1e293b; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <div style=""background: #0f172a; padding: 24px; text-align: center; border-radius: 8px 8px 0 0;"">
        <h1 style=""color: #38bdf8; margin: 0; font-size: 24px; letter-spacing: -0.5px;"">FlowOS</h1>
        <p style=""color: #94a3b8; margin: 6px 0 0 0; font-size: 13px;"">State-Machine-Governed Workflow Operating System</p>
    </div>
    <div style=""border: 1px solid #e2e8f0; border-top: none; padding: 28px; border-radius: 0 0 8px 8px; background: #ffffff;"">
        <h2 style=""color: #0f172a; margin-top: 0;"">You're invited to {tenant.Name}!</h2>
        <p>You've been invited to join <strong>{tenant.Name}</strong> on FlowOS as <strong>{user.Role}</strong>.</p>
        <p>Your 6-digit invite code is:</p>
        <div style=""background: #f0f9ff; border: 2px dashed #0284c7; padding: 16px; border-radius: 8px; font-family: monospace; font-size: 32px; font-weight: bold; text-align: center; color: #0284c7; letter-spacing: 6px; margin: 20px 0;"">
            {token}
        </div>
        <p>Open the FlowOS dashboard, choose &quot;Accept Invite&quot;, and enter this code with your email
        ({user.Email}) to set your own password and sign in.</p>
        <hr style=""border: none; border-top: 1px solid #e2e8f0; margin: 28px 0;"" />
        <p style=""color: #64748b; font-size: 12px; margin: 0;"">
            This invite code will expire in 24 hours.<br/>
            If you weren't expecting this, you can safely ignore this email, or reach us at <a href=""mailto:{officialEmail}"" style=""color: #2563eb;"">{officialEmail}</a>.
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
            _logger.LogError(ex, "Failed to send invite email to {Email}", user.Email);
        }
    }

    private async Task SendVerificationEmailInternalAsync(Tenant tenant, TenantUser user, string token, CancellationToken ct)
    {
        var officialEmail = GetOfficialEmail();
        var baseUrl = _configuration["FlowOS:Communications:Email:VerificationBaseUrl"]
                      ?? "https://flowosbd.com/verify-email";
        var verificationLink = $"{baseUrl}?token={token}&email={Uri.EscapeDataString(user.Email)}";

        var subject = $"Verify your FlowOS Tenant Account - {tenant.Name} (Code: {token})";

        var body = $"Welcome to FlowOS!\n\n" +
                   $"Thank you for registering your organisation '{tenant.Name}'.\n\n" +
                   $"Your 6-Digit Email Verification Code: {token}\n\n" +
                   $"Enter this code on the FlowOS verification page, or open the direct link below:\n{verificationLink}\n\n" +
                   $"This code will expire in 24 hours.\n\n" +
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
        <p>Your 6-digit verification code is:</p>
        <div style=""background: #f0f9ff; border: 2px dashed #0284c7; padding: 16px; border-radius: 8px; font-family: monospace; font-size: 32px; font-weight: bold; text-align: center; color: #0284c7; letter-spacing: 6px; margin: 20px 0;"">
            {token}
        </div>
        <p style=""text-align: center; margin: 24px 0;"">
            <a href=""{verificationLink}"" style=""background-color: #2563eb; color: #ffffff; padding: 12px 28px; text-decoration: none; border-radius: 6px; font-weight: 600; display: inline-block; font-size: 15px;"">Or Click Here to Verify Instantly</a>
        </p>
        <hr style=""border: none; border-top: 1px solid #e2e8f0; margin: 28px 0;"" />
        <p style=""color: #64748b; font-size: 12px; margin: 0;"">
            This verification code and link will expire in 24 hours.<br/>
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
