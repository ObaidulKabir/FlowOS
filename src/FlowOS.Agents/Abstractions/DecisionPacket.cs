using System;
using System.Collections.Generic;

namespace FlowOS.Agents.Abstractions;

public sealed record SlaReminderFact(string Duration, string TriggerEvent);

public sealed record AutoCommitPolicy(
    double MinConfidence,
    IReadOnlyList<string> AllowedEvents);

public sealed record AgentProviderRef(
    string Alias,
    string ProviderName,
    string? Model,
    string? Endpoint,
    bool HasApiKey);

public sealed record AgentToolDescriptor(
    string Name,
    string Kind,
    string? Provider,
    string Description,
    string SideEffect = "none",
    bool Prefetch = false,
    string? Capability = null);

public sealed record AgentPromptContext(
    string? TemplateGuideline,
    string? PolicyGuideline,
    string Objective,
    string? Alias = null,
    string? Title = null,
    string? System = null,
    string? Instructions = null);

public sealed record AgentPromptRef(
    string Alias,
    string? Title,
    string? System,
    string? Instructions);

public sealed record AgentDataContext(
    IReadOnlyDictionary<string, object?> CanonicalContext,
    IReadOnlyDictionary<string, object> EventPayloads,
    IReadOnlyList<SlaReminderFact> SlaReminders,
    string? TimeoutEvent,
    IReadOnlyDictionary<string, object?>? ToolResults = null);

public sealed record DecisionPacket(
    Guid TenantId,
    Guid WorkflowInstanceId,
    string CurrentStepId,
    string? CurrentState,
    string StepType,
    string Actor,
    string? TemplateGuideline,
    string? PolicyGuideline,
    IReadOnlyDictionary<string, object?> CanonicalContext,
    IReadOnlyList<string> LegalNextStepEvents,
    IReadOnlyList<string> LegalStateMachineEvents,
    IReadOnlyList<string> AllowedRoles,
    IReadOnlyList<SlaReminderFact> SlaReminders,
    string? TimeoutEvent,
    IReadOnlyDictionary<string, object> EventPayloads,
    string Objective,
    AutoCommitPolicy? AutoCommit,
    AgentProviderRef? Provider = null,
    IReadOnlyList<AgentToolDescriptor>? Tools = null,
    AgentPromptRef? PromptBinding = null,
    IReadOnlyDictionary<string, object?>? ToolResults = null)
{
    public IReadOnlyList<string> LegalEvents => LegalNextStepEvents;

    public AgentPromptContext Prompt => new(
        TemplateGuideline,
        PolicyGuideline,
        Objective,
        PromptBinding?.Alias,
        PromptBinding?.Title,
        PromptBinding?.System,
        PromptBinding?.Instructions);

    public AgentDataContext Data => new(
        CanonicalContext,
        EventPayloads,
        SlaReminders,
        TimeoutEvent,
        ToolResults);

    public IReadOnlyList<AgentToolDescriptor> DeclaredTools =>
        Tools ?? Array.Empty<AgentToolDescriptor>();
}
