using System;
using System.Threading.Tasks;
using FlowOS.Core.Interfaces;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.Services;

public class EventRegistry : IEventRegistry
{
    private readonly FlowOSDbContext _context;
    private readonly ILogger<EventRegistry> _logger;

    public EventRegistry(FlowOSDbContext context, ILogger<EventRegistry> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> ExistsAsync(string eventId, Guid tenantId)
    {
        return await _context.EventDefinitions
            .AnyAsync(e => e.EventId == eventId && e.TenantId == tenantId);
    }

    public async Task ValidateAsync(string eventId, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
             // Backward compatibility: If null/empty, we might skip validation if caller handles it.
             // But strict registry requires ID.
             throw new ArgumentException("Event ID cannot be null or empty.");
        }

        // Strict Mode Enforcement
        if (!eventId.StartsWith("EVT-"))
        {
            throw new ArgumentException($"Event ID '{eventId}' does not follow the naming convention 'EVT-*'.");
        }

        var exists = await ExistsAsync(eventId, tenantId);
        if (!exists)
        {
            _logger.LogWarning($"Event Validation Failed: '{eventId}' not found for tenant {tenantId}");
            throw new KeyNotFoundException($"Event '{eventId}' is not registered.");
        }
    }
}
