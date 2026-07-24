using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Security.Policies;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.Infrastructure.Services;

public class EfCorePolicyProvider : IPolicyProvider
{
    private readonly FlowOSDbContext _context;

    public EfCorePolicyProvider(FlowOSDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Policy>> GetApplicablePoliciesAsync(PolicyContext context)
    {
        if (!Guid.TryParse(context.TenantId, out var tenantGuid))
        {
            return Enumerable.Empty<Policy>();
        }

        var policyEntities = await _context.Policies
            .AsNoTracking()
            .Where(p => p.TenantId == tenantGuid)
            .ToListAsync();

        return policyEntities.Select(Map);
    }

    public async Task<IEnumerable<Policy>> GetAllPoliciesAsync()
    {
        var policyEntities = await _context.Policies
            .AsNoTracking()
            .ToListAsync();

        return policyEntities.Select(Map);
    }

    private static Policy Map(FlowOS.Security.Models.Policy p) => new()
    {
        PolicyId = p.Id.ToString(),
        Name = p.Name,
        Description = "Database Policy",
        Scope = "Workflow",
        ConditionJson = p.ConditionJson
    };
}
