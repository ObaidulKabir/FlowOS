using System;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Builders;

public class EventBuilder
{
    private readonly Guid _tenantId;
    private readonly string _eventId;
    private string _name;
    private string _description = string.Empty;
    private string _entityType = "System";
    private EventCategory _category = EventCategory.System;
    private bool _isTerminal;

    private EventBuilder(Guid tenantId, string eventId)
    {
        _tenantId = tenantId;
        _eventId = eventId;
        _name = eventId; // Default name to ID
    }

    public static EventBuilder Create(Guid tenantId, string eventId)
    {
        return new EventBuilder(tenantId, eventId);
    }

    public EventBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public EventBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public EventBuilder ForEntity(string entityType)
    {
        _entityType = entityType;
        return this;
    }

    public EventBuilder AsCategory(EventCategory category)
    {
        _category = category;
        return this;
    }

    public EventBuilder IsTerminal(bool isTerminal = true)
    {
        _isTerminal = isTerminal;
        return this;
    }

    public EventDefinition Build()
    {
        return new EventDefinition(
            _eventId,
            _tenantId,
            _name,
            _description,
            _entityType,
            _category,
            1,
            null,
            _isTerminal
        );
    }
}
