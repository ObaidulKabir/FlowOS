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
                Console.WriteLine($"[CapabilityService] Cache miss for {cacheKey}");
                var role = await _context.Roles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == roleName);
                
                rolePermissions = role?.Permissions ?? new HashSet<string>();
                Console.WriteLine($"[CapabilityService] Loaded {rolePermissions.Count} permissions for role {roleName} (Tenant: {tenantId}): {string.Join(", ", rolePermissions)}");
                
                // If role was not found in DB, don't cache empty permissions forever (maybe it's not seeded yet)
                // But for now we cache it.
                // FIX: If we just seeded it, it might not be committed if we are in the same scope, but here we are in a new request.
                // However, if we are running in Memory, maybe it persists?
                
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromSeconds(10)); // Short cache for dev
                
                _cache.Set(cacheKey, rolePermissions, cacheOptions);
            }
            else
            {
                Console.WriteLine($"[CapabilityService] Cache hit for {cacheKey}: {rolePermissions.Count} permissions");
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
