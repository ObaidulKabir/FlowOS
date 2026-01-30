using System;
using System.Threading.Tasks;
using FlowOS.Core.Interfaces;
using FlowOS.Workflows.Domain;

namespace FlowOS.Workflows.Loader;

public class WorkflowDefinitionLoader
{
    private readonly IEventRegistry _eventRegistry;

    public WorkflowDefinitionLoader(IEventRegistry eventRegistry)
    {
        _eventRegistry = eventRegistry;
    }

    public async Task ValidateAsync(WorkflowDefinition definition)
    {
        foreach (var step in definition.Steps)
        {
            foreach (var eventKey in step.NextSteps.Keys)
            {
                // Check if it looks like an EventId (simple heuristic or check existence)
                // In a strict mode, we would enforce this.
                // For Phase 1: We try to validate. If it exists in registry, great. 
                // If not, we assume it's a legacy string event (unless we want to enforce strictness now).
                
                // Strategy: Check if it exists. If yes, it's valid.
                // If no, is it a legacy event? (We might need a flag or heuristic).
                // For now, let's assume if it looks like an ID (e.g. EVT-*) it must exist.
                
                if (eventKey.StartsWith("EVT-"))
                {
                    await _eventRegistry.ValidateAsync(eventKey, definition.TenantId);
                }
                
                // If it doesn't start with EVT-, it might be a legacy string "Approved".
                // We permit it for now (Phase 1).
            }
        }
    }
}
