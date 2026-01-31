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

        // Fetch policies for this tenant
        // Note: FlowOS.Security.Models.Policy (Entity) != FlowOS.Security.Policies.Policy (Domain)
        // We need to map them.
        
        var policyEntities = await _context.Policies
            .AsNoTracking()
            .Where(p => p.TenantId == tenantGuid)
            .ToListAsync();

        var policies = policyEntities.Select(p => new Policy
        {
            PolicyId = p.Id.ToString(),
            Name = p.Name,
            // Assuming Scope/Description mapping or defaulting
            Description = "Database Policy", 
            Scope = "Workflow" // Default scope or derived from JSON
            // Note: The actual rules/conditions need to be parsed by Evaluator
            // Current PolicyEvaluator logic is very simple (only checks Name == "DenyAll").
            // We need to enhance PolicyEvaluator to parse ConditionJson or passing it along.
        });

        return policies;
    }

    public async Task<IEnumerable<Policy>> GetAllPoliciesAsync()
    {
         var policyEntities = await _context.Policies
            .AsNoTracking()
            .ToListAsync();
            
         return policyEntities.Select(p => new Policy
        {
            PolicyId = p.Id.ToString(),
            Name = p.Name,
            Description = "Database Policy"
        });
    }
}
