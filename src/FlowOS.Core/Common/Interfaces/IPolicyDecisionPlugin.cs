using System;
using System.Collections.Generic;

namespace FlowOS.Core.Common.Interfaces;

public sealed record PolicyDecisionPluginContext(
    Guid TenantId,
    Guid WorkflowInstanceId,
    string StepId,
    string EventType,
    IReadOnlyDictionary<string, string> Conditions,
    Dictionary<string, object>? Payload);

public sealed record PolicyDecisionPluginResult(
    bool IsMatched,
    string? NextStepId,
    string? Reason = null);

public interface IPolicyDecisionPlugin
{
    string ProviderName { get; }

    PolicyDecisionPluginResult Evaluate(PolicyDecisionPluginContext context);
}

public interface IPolicyDecisionPluginRegistry
{
    bool TryResolve(string providerName, out IPolicyDecisionPlugin plugin);
}
