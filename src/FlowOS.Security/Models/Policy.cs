using System;
using System.Text.Json;

namespace FlowOS.Security.Models;

public class Policy
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string ConditionJson { get; private set; }

    protected Policy() 
    {
        Name = null!;
        ConditionJson = "{}";
    }

    public Policy(Guid tenantId, string name, string conditionJson)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
        
        Id = Guid.NewGuid();
        TenantId = tenantId;
        Name = name;
        ConditionJson = conditionJson;
    }
}
