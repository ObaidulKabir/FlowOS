using FlowOS.Agents.Abstractions;

namespace FlowOS.Application.Services;

/// <summary>
/// Projects a DecisionPacket into the composed Agent Context the MCP tools return:
/// Prompt + Data + Tools + Provider on one object. API keys and URLs stay off the payload.
/// </summary>
public static class AgentContextComposer
{
    public static ComposedAgentContext FromPacket(
        DecisionPacket packet,
        string mode,
        Guid? workflowClassId = null,
        bool prefetched = false,
        bool hideInstance = false) =>
        new(
            mode: mode,
            tenantId: packet.TenantId,
            workflowInstanceId: hideInstance || packet.WorkflowInstanceId == Guid.Empty
                ? (Guid?)null
                : packet.WorkflowInstanceId,
            workflowClassId: workflowClassId,
            currentStepId: packet.CurrentStepId,
            currentState: packet.CurrentState,
            stepType: packet.StepType,
            actor: packet.Actor,
            prompt: new ComposedPromptContext(
                alias: packet.Prompt.Alias,
                title: packet.Prompt.Title,
                system: packet.Prompt.System,
                instructions: packet.Prompt.Instructions,
                templateGuideline: packet.Prompt.TemplateGuideline,
                policyGuideline: packet.Prompt.PolicyGuideline,
                objective: packet.Prompt.Objective),
            data: new ComposedDataContext(
                canonicalContext: packet.CanonicalContext,
                eventPayloads: packet.EventPayloads,
                slaReminders: packet.Data.SlaReminders,
                timeoutEvent: packet.Data.TimeoutEvent,
                toolResults: packet.ToolResults),
            tools: packet.DeclaredTools.Select(tool => new ComposedToolDescriptor(
                name: tool.Name,
                kind: tool.Kind,
                provider: tool.Provider,
                capability: tool.Capability,
                sideEffect: tool.SideEffect,
                prefetch: tool.Prefetch,
                description: tool.Description)).ToList(),
            provider: packet.Provider == null
                ? null
                : new ComposedProviderRef(
                    alias: packet.Provider.Alias,
                    providerName: packet.Provider.ProviderName,
                    model: packet.Provider.Model,
                    endpoint: packet.Provider.Endpoint,
                    hasApiKey: packet.Provider.HasApiKey),
            autoCommit: packet.AutoCommit == null
                ? null
                : new ComposedAutoCommitPolicy(
                    minConfidence: packet.AutoCommit.MinConfidence,
                    allowedEvents: packet.AutoCommit.AllowedEvents),
            legalNextStepEvents: packet.LegalNextStepEvents,
            legalStateMachineEvents: packet.LegalStateMachineEvents,
            prefetched: prefetched,
            resolved: new ComposedResolvedFlags(
                prompt: packet.PromptBinding != null,
                provider: packet.Provider != null));
}
