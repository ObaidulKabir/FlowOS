using FlowOS.Agents.Abstractions;

namespace FlowOS.Application.Common.Interfaces;

public sealed record CapabilityInvokeResult(
    bool Ok,
    int? StatusCode,
    string? Body,
    object? Parsed,
    string? Error);

public interface ICapabilityInvoker
{
    Task<CapabilityInvokeResult> InvokeAsync(
        Guid tenantId,
        string capabilityName,
        string operation,
        object? payload,
        Guid? workflowInstanceId = null,
        string? stepId = null,
        CancellationToken cancellationToken = default);
}

public sealed record AgentResourceRequest(
    Guid TenantId,
    Guid WorkflowInstanceId,
    string StepId,
    string ToolName,
    string CapabilityName,
    IReadOnlyDictionary<string, object?> CanonicalContext,
    IReadOnlyDictionary<string, object> EventPayloads);

public sealed record AgentResourceResult(
    bool Ok,
    string ToolName,
    string CapabilityName,
    object? Data,
    string? Error);

public interface IAgentResourcePlugin
{
    string Name { get; }
    string Category { get; }
    string Operation { get; }
    string SideEffect { get; }
    bool Prefetch { get; }
    string Description { get; }

    Task<AgentResourceResult> ExecuteAsync(
        AgentResourceRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAgentToolHost
{
    Task<DecisionPacket> PrefetchAsync(
        DecisionPacket packet,
        CancellationToken cancellationToken = default);
}
