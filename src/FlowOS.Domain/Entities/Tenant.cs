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

    // Constructor for EF Core
    protected Tenant() 
    {
        Name = null!;
        ConfigurationJson = null!;
    }

    public Tenant(string name, string configurationJson = "{}")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        TenantId = Guid.NewGuid();
        Name = name;
        Status = TenantStatus.Active;
        ConfigurationJson = configurationJson;
        CreatedAt = DateTime.UtcNow;
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
}
