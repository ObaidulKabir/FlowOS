using System;
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

public interface ITenantAuthService
{
    Task<RegisterTenantUserResult> RegisterTenantAsync(RegisterTenantUserRequest request, CancellationToken ct = default);
    Task<VerifyEmailResult> VerifyEmailAsync(string email, string token, CancellationToken ct = default);
    Task<VerifyEmailResult> VerifyEmailWithPasswordAsync(string email, string password, CancellationToken ct = default);
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<ResendVerificationResult> ResendVerificationEmailAsync(string email, CancellationToken ct = default);
    Task<TenantUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
}
