using System;
using System.Security.Cryptography;
using System.Text;

namespace FlowOS.Domain.Entities;

public class TenantApiKey
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string KeyPrefix { get; private set; }
    public string KeyHash { get; private set; }
    public string MaskedKey { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }
    public bool IsRevoked { get; private set; }

    // Constructor for EF Core
    protected TenantApiKey()
    {
        Name = null!;
        KeyPrefix = null!;
        KeyHash = null!;
        MaskedKey = null!;
    }

    public TenantApiKey(Guid tenantId, string name, string rawKey)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        if (string.IsNullOrWhiteSpace(rawKey))
            throw new ArgumentNullException(nameof(rawKey));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name.Trim();
        KeyHash = HashKey(rawKey);
        KeyPrefix = rawKey.Length > 12 ? rawKey.Substring(0, 12) : rawKey;
        MaskedKey = MaskRawKey(rawKey);
        CreatedAt = DateTime.UtcNow;
        IsRevoked = false;
    }

    public void RecordUsage()
    {
        LastUsedAt = DateTime.UtcNow;
    }

    public void Revoke()
    {
        IsRevoked = true;
    }

    public static string HashKey(string rawKey)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string GenerateRawKey(string prefix = "flw_live_")
    {
        var randomBytes = new byte[24];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var base64 = Convert.ToBase64String(randomBytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
        return $"{prefix}{base64}";
    }

    private static string MaskRawKey(string rawKey)
    {
        if (rawKey.Length <= 8)
            return "••••••••";

        var start = rawKey.Length >= 8 ? rawKey.Substring(0, 8) : rawKey.Substring(0, 4);
        var end = rawKey.Substring(rawKey.Length - 4);
        return $"{start}••••••••{end}";
    }
}
