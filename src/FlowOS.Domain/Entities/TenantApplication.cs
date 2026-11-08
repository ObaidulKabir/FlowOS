using System;

namespace FlowOS.Domain.Entities;

/// <summary>
/// An application a tenant builds on FlowOS. It groups the application's FlowOS API keys and (in later
/// changes) its workflows and AI agents. AI provider configuration, including model API keys, stays
/// tenant-level so a provider key is rotated once for every application.
/// Named TenantApplication because a type called Application would collide with the
/// FlowOS.Application namespace inside every FlowOS.* namespace.
/// </summary>
public sealed class TenantApplication
{
    public const int NameMaxLength = 150;
    public const int DescriptionMaxLength = 1000;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private TenantApplication()
    {
    }

    public TenantApplication(Guid tenantId, string name, string? description, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void Update(string name, string? description, DateTime nowUtc)
    {
        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
        UpdatedAtUtc = nowUtc;
    }

    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("Application name is required.", nameof(name));
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Application name must be {NameMaxLength} characters or fewer.", nameof(name));
        return trimmed;
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.Length > DescriptionMaxLength)
            throw new ArgumentException($"Application description must be {DescriptionMaxLength} characters or fewer.", nameof(description));
        return trimmed;
    }
}
