using FlowOS.Agents.Abstractions;

namespace FlowOS.Application.Services;

/// <summary>
/// Projects a DecisionPacket into the composed Agent Context the MCP tools return:
/// Prompt + Data + Tools + Provider on one object. API keys and URLs stay off the payload.
/// </summary>
public static class AgentContextComposer
{
    public static object FromPacket(
        DecisionPacket packet,
        string mode,
        Guid? workflowClassId = null,
        bool prefetched = false,
        bool hideInstance = false) =>
        new
        {
            mode,
            tenantId = packet.TenantId,
            workflowInstanceId = hideInstance || packet.WorkflowInstanceId == Guid.Empty
                ? (Guid?)null
                : packet.WorkflowInstanceId,
            workflowClassId,
            currentStepId = packet.CurrentStepId,
            currentState = packet.CurrentState,
            stepType = packet.StepType,
            actor = packet.Actor,
            prompt = new
            {
                alias = packet.Prompt.Alias,
                title = packet.Prompt.Title,
                system = packet.Prompt.System,
                instructions = packet.Prompt.Instructions,
                templateGuideline = packet.Prompt.TemplateGuideline,
                policyGuideline = packet.Prompt.PolicyGuideline,
                objective = packet.Prompt.Objective
            },
            data = new
            {
                canonicalContext = packet.CanonicalContext,
                eventPayloads = packet.EventPayloads,
                slaReminders = packet.Data.SlaReminders,
                timeoutEvent = packet.Data.TimeoutEvent,
                toolResults = packet.ToolResults
            },
            tools = packet.DeclaredTools.Select(tool => new
            {
                name = tool.Name,
                kind = tool.Kind,
                provider = tool.Provider,
                capability = tool.Capability,
                sideEffect = tool.SideEffect,
                prefetch = tool.Prefetch,
                description = tool.Description
            }),
            provider = packet.Provider == null
                ? null
                : new
                {
                    alias = packet.Provider.Alias,
                    providerName = packet.Provider.ProviderName,
                    model = packet.Provider.Model,
                    endpoint = packet.Provider.Endpoint,
                    hasApiKey = packet.Provider.HasApiKey
                },
            autoCommit = packet.AutoCommit == null
                ? null
                : new
                {
                    minConfidence = packet.AutoCommit.MinConfidence,
                    allowedEvents = packet.AutoCommit.AllowedEvents
                },
            legalNextStepEvents = packet.LegalNextStepEvents,
            legalStateMachineEvents = packet.LegalStateMachineEvents,
            prefetched,
            resolved = new
            {
                prompt = packet.PromptBinding != null,
                provider = packet.Provider != null
            }
        };
}
