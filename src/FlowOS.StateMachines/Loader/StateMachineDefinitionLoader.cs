using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Core.Interfaces;
using FlowOS.Domain.Entities;

namespace FlowOS.StateMachines.Loader;

public class StateMachineDefinitionLoader
{
    private readonly IEventRegistry _eventRegistry;

    public StateMachineDefinitionLoader(IEventRegistry eventRegistry)
    {
        _eventRegistry = eventRegistry;
    }

    public async Task ValidateAsync(StateMachineDefinition definition)
    {
        foreach (var transition in definition.Transitions)
        {
            // If EventId is present, validate it against the registry
            if (!string.IsNullOrEmpty(transition.EventId))
            {
                await _eventRegistry.ValidateAsync(transition.EventId, definition.TenantId);
            }
            // If TriggerEventType is used (legacy), we just log a warning in a real system,
            // but here we allow it for backward compatibility as per Phase 1.
        }
    }
}
