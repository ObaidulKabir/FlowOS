using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Application.Common.Interfaces;

public record RegisterTenantUserRequest(
    string TenantName,
    string Email,
    string Password,
    string? FullName = null);

public record RegisterTenantUserResult(
    bool Success,
    string Message,
    Guid? TenantId = null,
    string? TenantName = null,
    string? Email = null,
    bool IsEmailVerified = false,
    string? VerificationToken = null);

public record VerifyEmailRequest(
    string Email,
    string Token);

public record VerifyEmailResult(
    bool Success,
    string Message,
    Guid? TenantId = null,
    string? Email = null,
    bool IsEmailVerified = false);

public record LoginRequest(
    string Email,
    string Password);

public record TenantUserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    Guid TenantId,
    string TenantName,
    bool IsEmailVerified,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    string Plan = "Trial",
    string BillingStatus = "Unpaid",
    bool CanRunRuntime = false);

public record LoginResult(
    bool Success,
    string Message,
    string? Token = null,
    string? TokenType = null,
    int? ExpiresIn = null,
    TenantUserDto? User = null,
    string? ErrorCode = null);

public record VerifyEmailWithPasswordRequest(
    string Email,
    string Password);

public record ResendVerificationResult(
    bool Success,
    string Message,
    string? VerificationToken = null);

/// <summary>
/// An organization's own teammate login, distinct from its shared TenantApiKey(s) - see
/// docs/29-organizational-team-access.md. Invited by an existing Admin/SuperAdmin TenantUser of the
/// same tenant; the invited person accepts the invite and sets their own password, so no one else
/// ever knows it.
/// </summary>
public record InviteTenantUserRequest(
    Guid TenantId,
    string Email,
    string FullName,
    string Role);

public record InviteTenantUserResult(
    bool Success,
    string Message,
    Guid? UserId = null,
    string? Email = null,
    string? InviteToken = null);

public record AcceptInviteRequest(
    string Email,
    string Token,
    string Password);

public record AcceptInviteResult(
    bool Success,
    string Message,
    Guid? TenantId = null,
    string? Email = null);

public record TeammateActionResult(
    bool Success,
    string Message);

public record TenantTeammateDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    bool IsEmailVerified,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

public interface ITenantAuthService
{
    Task<RegisterTenantUserResult> RegisterTenantAsync(RegisterTenantUserRequest request, CancellationToken ct = default);
    Task<VerifyEmailResult> VerifyEmailAsync(string email, string token, CancellationToken ct = default);
    Task<VerifyEmailResult> VerifyEmailWithPasswordAsync(string email, string password, CancellationToken ct = default);
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<ResendVerificationResult> ResendVerificationEmailAsync(string email, CancellationToken ct = default);
    Task<TenantUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new teammate login in an existing, already-registered tenant. The teammate's password
    /// is never set by the inviter - they accept the invite (AcceptInviteAsync) and choose it themselves.
    /// </summary>
    Task<InviteTenantUserResult> InviteUserAsync(InviteTenantUserRequest request, CancellationToken ct = default);

    /// <summary>Accepts a pending invite: verifies the invite token and sets the teammate's own password.</summary>
    Task<AcceptInviteResult> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken ct = default);

    /// <summary>Lists the teammate logins (TenantUser rows) of one tenant, most recently created first.</summary>
    Task<IReadOnlyList<TenantTeammateDto>> ListTeammatesAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Removes a teammate's login. Fails (Success: false) rather than throwing when userId isn't in this
    /// tenant, or is the tenant's last remaining Admin/SuperAdmin - a tenant can never be left with no one
    /// able to manage it.
    /// </summary>
    Task<TeammateActionResult> RemoveTeammateAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
