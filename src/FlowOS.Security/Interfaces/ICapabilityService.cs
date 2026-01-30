using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FlowOS.Security.Interfaces;

public interface ICapabilityService
{
    Task<HashSet<string>> GetCapabilitiesAsync(Guid tenantId, IEnumerable<string> roles);
}
