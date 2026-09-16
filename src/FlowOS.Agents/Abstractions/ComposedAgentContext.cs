using System;
using System.Collections.Generic;

namespace FlowOS.Agents.Abstractions;

public sealed record ComposedPromptContext(
    string? alias,
    string? title,
    string? system,
    string? instructions,
    string? templateGuideline,
    string? policyGuideline,
    string objective);

public sealed record ComposedDataContext(
    IReadOnlyDictionary<string, object?> canonicalContext,
    IReadOnlyDictionary<string, object> eventPayloads,
    IReadOnlyList<SlaReminderFact> slaReminders,
    string? timeoutEvent,
    IReadOnlyDictionary<string, object?>? toolResults);

public sealed record ComposedToolDescriptor(
    string name,
    string kind,
    string? provider,
    string? capability,
    string sideEffect,
    bool prefetch,
    string description);

public sealed record ComposedProviderRef(
    string alias,
    string providerName,
    string? model,
    string? endpoint,
    bool hasApiKey);

public sealed record ComposedAutoCommitPolicy(
    double minConfidence,
    IReadOnlyList<string> allowedEvents);

public sealed record ComposedResolvedFlags(
    bool prompt,
    bool provider);

public sealed record ComposedAgentContext(
    string mode,
    Guid tenantId,
    Guid? workflowInstanceId,
    Guid? workflowClassId,
    string currentStepId,
    string? currentState,
    string stepType,
    string actor,
    ComposedPromptContext prompt,
    ComposedDataContext data,
    IReadOnlyList<ComposedToolDescriptor> tools,
    ComposedProviderRef? provider,
    ComposedAutoCommitPolicy? autoCommit,
    IReadOnlyList<string> legalNextStepEvents,
    IReadOnlyList<string> legalStateMachineEvents,
    bool prefetched,
    ComposedResolvedFlags resolved);
