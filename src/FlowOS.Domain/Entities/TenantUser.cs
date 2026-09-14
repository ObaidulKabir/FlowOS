using System;
using System.Security.Cryptography;

namespace FlowOS.Domain.Entities;

public class TenantUser
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Email { get; private set; }
    public string PasswordHash { get; private set; }
    public string FullName { get; private set; }
    public string Role { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public string? EmailVerificationToken { get; private set; }
    public DateTime? EmailVerificationTokenExpiresAt { get; private set; }
    public DateTime? EmailVerifiedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }

    // Navigation property
    public virtual Tenant? Tenant { get; private set; }

    // EF Core constructor
    protected TenantUser()
    {
        Email = null!;
        PasswordHash = null!;
        FullName = null!;
        Role = "Admin";
    }

    public TenantUser(
        Guid tenantId,
        string email,
        string passwordHash,
        string fullName,
        string role = "Admin",
        bool isEmailVerified = false)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentNullException(nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentNullException(nameof(passwordHash));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
        FullName = string.IsNullOrWhiteSpace(fullName) ? Email : fullName.Trim();
        Role = string.IsNullOrWhiteSpace(role) ? "Admin" : role.Trim();
        IsEmailVerified = isEmailVerified;
        CreatedAt = DateTime.UtcNow;

        if (!isEmailVerified)
        {
            GenerateVerificationToken(TimeSpan.FromHours(24));
        }
    }

    public string GenerateVerificationToken(TimeSpan? lifetime = null)
    {
        // Cryptographically secure 6-digit numeric OTP code for ease of entry
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");

        EmailVerificationToken = code;
        EmailVerificationTokenExpiresAt = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(24));
        UpdatedAt = DateTime.UtcNow;

        return code;
    }

    public bool VerifyEmail(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (IsEmailVerified)
            return true;

        if (string.IsNullOrWhiteSpace(EmailVerificationToken) ||
            !string.Equals(EmailVerificationToken.Trim(), token.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (EmailVerificationTokenExpiresAt.HasValue && EmailVerificationTokenExpiresAt.Value < DateTime.UtcNow)
        {
            return false;
        }

        IsEmailVerified = true;
        EmailVerifiedAt = DateTime.UtcNow;
        EmailVerificationToken = null;
        EmailVerificationTokenExpiresAt = null;
        UpdatedAt = DateTime.UtcNow;

        return true;
    }

    public void MarkEmailAsVerified()
    {
        IsEmailVerified = true;
        EmailVerifiedAt = DateTime.UtcNow;
        EmailVerificationToken = null;
        EmailVerificationTokenExpiresAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdatePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new ArgumentNullException(nameof(newPasswordHash));

        PasswordHash = newPasswordHash;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordLogin()
    {
        LastLoginAt = DateTime.UtcNow;
    }
}
