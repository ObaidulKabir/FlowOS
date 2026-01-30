using System;
using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Entities;

public class EventDefinition
{
    public string EventId { get; private set; } // Stable, Unique ID (e.g., EVT-ORDER-APPROVED)
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } // Human readable name
    public string Description { get; private set; }
    public string EntityType { get; private set; } // E.g., "Order", "User"
    public int Version { get; private set; }
    public EventCategory Category { get; private set; }
    public StateMachineStatus Status { get; private set; } // Added Status
    public string? PayloadSchema { get; private set; } // Optional JSON Schema
    public bool IsTerminal { get; private set; }
    
    public DateTime CreatedAt { get; private set; }

    // EF Core Constructor
    protected EventDefinition() 
    {
        EventId = null!;
        Name = null!;
        Description = null!;
        EntityType = null!;
    }

    public EventDefinition(
        string eventId, 
        Guid tenantId, 
        string name, 
        string description, 
        string entityType, 
        EventCategory category, 
        int version = 1,
        string? payloadSchema = null,
        bool isTerminal = false)
    {
        if (string.IsNullOrWhiteSpace(eventId)) throw new ArgumentNullException(nameof(eventId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));

        EventId = eventId;
        TenantId = tenantId;
        Name = name;
        Description = description;
        EntityType = entityType;
        Category = category;
        Version = version;
        Status = StateMachineStatus.Draft; // Default to Draft
        PayloadSchema = payloadSchema;
        IsTerminal = isTerminal;
        CreatedAt = DateTime.UtcNow;
    }

    public void Publish()
    {
        Status = StateMachineStatus.Published;
    }
}
