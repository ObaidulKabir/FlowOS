using System.Collections.Generic;
using System.Threading.Tasks;

namespace FlowOS.Security.Policies;

public class AllowAllPolicyProvider : IPolicyProvider
{
    public Task<IEnumerable<Policy>> GetApplicablePoliciesAsync(PolicyContext context)
    {
        return Task.FromResult<IEnumerable<Policy>>(new List<Policy>());
    }
}
