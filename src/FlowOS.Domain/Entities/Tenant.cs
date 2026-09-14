using System;
using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Entities;

public class Tenant
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public TenantStatus Status { get; private set; }
    public string ConfigurationJson { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public string? WebhookSigningSecret { get; private set; }

    // Constructor for EF Core
    protected Tenant() 
    {
        Name = null!;
        ConfigurationJson = null!;
    }

    public Tenant(string name, string configurationJson = "{}")
        : this(name, TenantStatus.Active, configurationJson)
    {
    }

    public Tenant(string name, TenantStatus initialStatus, string configurationJson = "{}")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        TenantId = Guid.NewGuid();
        Name = name;
        Status = initialStatus;
        ConfigurationJson = configurationJson;
        CreatedAt = DateTime.UtcNow;

        var randomBytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        WebhookSigningSecret = $"whsec_{Convert.ToHexString(randomBytes).ToLowerInvariant()}";
    }

    public void SetWebhookSigningSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Webhook signing secret cannot be empty.", nameof(secret));
        WebhookSigningSecret = secret;
        UpdatedAt = DateTime.UtcNow;
    }

    public string RotateWebhookSigningSecret()
    {
        var randomBytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var hex = Convert.ToHexString(randomBytes).ToLowerInvariant();
        WebhookSigningSecret = $"whsec_{hex}";
        UpdatedAt = DateTime.UtcNow;
        return WebhookSigningSecret;
    }

    public void UpdateConfiguration(string newConfigJson)
    {
        ConfigurationJson = newConfigJson;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Suspend()
    {
        Status = TenantStatus.Suspended;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        Status = TenantStatus.Active;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkPendingVerification()
    {
        Status = TenantStatus.PendingVerification;
        UpdatedAt = DateTime.UtcNow;
    }
}
