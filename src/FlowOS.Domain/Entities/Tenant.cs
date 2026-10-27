using System;
using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Entities;

public class Tenant
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public TenantStatus Status { get; private set; }
    public TenantPlan Plan { get; private set; }
    public TenantBillingStatus BillingStatus { get; private set; }
    public string ConfigurationJson { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public string? WebhookSigningSecret { get; private set; }

    public bool? ExternalAIAgentEnabled { get; private set; }
    public bool? ExternalAIAgentAutoPilot { get; private set; }
    public string? ExternalAIAgentProfileId { get; private set; }

    /// <summary>
    /// CR-11: when false, no agent suggestion is auto-committed for this tenant, whatever the step or agent
    /// profile allows; suggestions are parked for a person. Existing tenants keep true.
    /// </summary>
    public bool AgentAutoCommitEnabled { get; private set; } = true;
    /// CR-14: the calling system's own reference for this tenant (for example a Learning OS institution
    /// path), so it can find or provision its tenant without storing FlowOS ids. Unique when set.
    /// </summary>
    public string? ExternalTenantRef { get; private set; }

    public const int ExternalTenantRefMaxLength = 200;

    public void SetExternalTenantRef(string? reference)
    {
        var trimmed = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (trimmed?.Length > ExternalTenantRefMaxLength)
            throw new ArgumentException($"External tenant reference must be at most {ExternalTenantRefMaxLength} characters.", nameof(reference));
        ExternalTenantRef = trimmed;
    }

    public bool CanRunRuntime =>
        (Plan == TenantPlan.Managed || Plan == TenantPlan.Enterprise) &&
        BillingStatus == TenantBillingStatus.Active;

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
        Plan = TenantPlan.Trial;
        BillingStatus = TenantBillingStatus.Unpaid;
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

    public void AssignPlan(TenantPlan plan, TenantBillingStatus billingStatus)
    {
        if (plan == TenantPlan.None)
            throw new ArgumentOutOfRangeException(nameof(plan), "A commercial plan is required.");

        Plan = plan;
        BillingStatus = billingStatus;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetAgentAutoCommit(bool enabled, DateTime changedAtUtc)
    {
        AgentAutoCommitEnabled = enabled;
        UpdatedAt = changedAtUtc;
    }

    public void SetExternalAIAgent(bool enabled, bool auto, string? profileId)
    {
        ExternalAIAgentEnabled = enabled;
        ExternalAIAgentAutoPilot = auto;
        ExternalAIAgentProfileId = string.IsNullOrWhiteSpace(profileId) ? null : profileId.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
