using System;
using System.Collections.Generic;

namespace FlowOS.Security.Models;

public class Role
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public HashSet<string> Permissions { get; private set; }

    protected Role() 
    {
        Name = null!;
        Permissions = new HashSet<string>();
    }

    public Role(Guid tenantId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name;
        Permissions = new HashSet<string>();
    }

    public void AddPermission(string permission)
    {
        if (!string.IsNullOrWhiteSpace(permission))
        {
            Permissions.Add(permission);
        }
    }
}
