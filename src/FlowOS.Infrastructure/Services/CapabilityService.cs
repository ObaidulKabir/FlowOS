using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Security.Interfaces; // Updated namespace
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FlowOS.Infrastructure.Services;

public class CapabilityService : ICapabilityService
{
    private readonly FlowOSDbContext _context;
    private readonly IMemoryCache _cache;

    public CapabilityService(FlowOSDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<HashSet<string>> GetCapabilitiesAsync(Guid tenantId, IEnumerable<string> roles)
    {
        if (roles == null || !roles.Any())
        {
            return new HashSet<string>();
        }

        var capabilities = new HashSet<string>();
        
        foreach (var roleName in roles)
        {
            var cacheKey = $"roles:{tenantId}:{roleName}";
            
            if (!_cache.TryGetValue(cacheKey, out HashSet<string>? rolePermissions))
            {
                var role = await _context.Roles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == roleName);
                
                rolePermissions = role?.Permissions ?? new HashSet<string>();
                
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(10)); // Cache for 10 mins
                
                _cache.Set(cacheKey, rolePermissions, cacheOptions);
            }

            if (rolePermissions != null)
            {
                foreach (var perm in rolePermissions)
                {
                    capabilities.Add(perm);
                }
            }
        }

        return capabilities;
    }
}
