using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FlowOS.Agents.Abstractions;

namespace FlowOS.Agents.Implementations;

/// <summary>
/// Implements priority-based token budgeting to prevent exceeding LLM context windows.
/// Uses character count as a proxy for tokens (approx 4 chars = 1 token).
/// </summary>
public static class TokenBudget
{
    private const int CharsPerToken = 4;
    // Default safe budget: ~80K characters = ~20K tokens.
    // Fits easily in standard 128K context windows while leaving room for output.
    public const int DefaultCharacterBudget = 80000;

    public static string BuildBudgetedPrompt(
        AgentContext context,
        int characterBudget = DefaultCharacterBudget)
    {
        var packet = context.Packet;
        var instructions = packet?.Prompt.Instructions
            ?? packet?.Objective
            ?? context.Objective;
        
        var declaredTools = packet?.DeclaredTools;
        var toolsPayload = declaredTools != null && declaredTools.Count > 0
            ? declaredTools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                parameters = t.ParametersSchema,
                sideEffect = t.SideEffect
            }).ToList()
            : null;

        var promptData = new Dictionary<string, object?>();
        promptData["instructions"] = instructions;
        promptData["templateGuideline"] = packet?.Prompt.TemplateGuideline;
        promptData["policyGuideline"] = packet?.Prompt.PolicyGuideline;
        promptData["currentStepId"] = packet?.CurrentStepId;
        promptData["currentState"] = packet?.CurrentState;
        
        // Critical data that is always included
        promptData["data"] = packet?.CanonicalContext;
        promptData["tools"] = toolsPayload;
        promptData["snapshot"] = context.EntitySnapshot;

        // Try adding full payloads and tool results
        var eventPayloads = CloneEventPayloads(packet?.EventPayloads);
        var toolResults = CloneDictionary(packet?.ToolResults);
        
        promptData["eventPayloads"] = eventPayloads;
        promptData["toolResults"] = toolResults;

        var json = JsonSerializer.Serialize(promptData);
        if (json.Length <= characterBudget)
        {
            return json;
        }

        // Budget exceeded. Start shedding less critical data.
        // Priority 1: Shed older event payloads.
        if (eventPayloads != null && eventPayloads.Count > 0)
        {
            // EventPayloads are often keyed by EventType. 
            // In FlowOS, AggregateEventPayloads already flattened it.
            // If it's too big, we just drop the event payloads entirely since we only have a flat dictionary 
            // and don't have temporal ordering in the dictionary keys.
            // (Alternatively, we could drop the largest keys, but dropping all is safer if budget is tight).
            promptData.Remove("eventPayloads");
            json = JsonSerializer.Serialize(promptData);
            if (json.Length <= characterBudget)
            {
                return json;
            }
        }

        // Priority 2: Shed tool results.
        if (toolResults != null && toolResults.Count > 0)
        {
            promptData.Remove("toolResults");
            json = JsonSerializer.Serialize(promptData);
            if (json.Length <= characterBudget)
            {
                return json;
            }
        }

        // Priority 3: Shed data (CanonicalContext).
        if (packet?.CanonicalContext != null)
        {
            promptData.Remove("data");
            json = JsonSerializer.Serialize(promptData);
            if (json.Length <= characterBudget)
            {
                return json;
            }
        }

        // If it's still too large, return the bare minimum (instructions, state, snapshot)
        return json;
    }

    private static Dictionary<string, object?>? CloneDictionary(IReadOnlyDictionary<string, object?>? source)
    {
        if (source == null) return null;
        return source.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private static Dictionary<string, object?>? CloneEventPayloads(IReadOnlyDictionary<string, object>? source)
    {
        if (source == null) return null;
        return source.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
    }
}
