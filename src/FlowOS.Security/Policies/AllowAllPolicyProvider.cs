using System.Collections.Generic;
using System.Threading.Tasks;

namespace FlowOS.Security.Policies;

public class AllowAllPolicyProvider : IPolicyProvider
{
    public Task<IEnumerable<Policy>> GetApplicablePoliciesAsync(PolicyContext context)
    {
        return Task.FromResult<IEnumerable<Policy>>(new List<Policy>());
    }

    public Task<IEnumerable<Policy>> GetAllPoliciesAsync()
    {
        // For AllowAll, we can return a dummy policy or empty
        return Task.FromResult<IEnumerable<Policy>>(new List<Policy> 
        {
            new Policy("AllowAll", "System", "Default allow-all policy")
        });
    }
}
