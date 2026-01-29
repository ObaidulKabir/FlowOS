using System.Collections.Generic;
using System.Threading.Tasks;

namespace FlowOS.Security.Policies;

public interface IPolicyProvider
{
    Task<IEnumerable<Policy>> GetApplicablePoliciesAsync(PolicyContext context);
}
