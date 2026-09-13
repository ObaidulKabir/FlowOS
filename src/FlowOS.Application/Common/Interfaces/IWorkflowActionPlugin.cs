using System;
using System.Collections.Generic;
using FlowOS.Workflows.Domain;

namespace FlowOS.Application.Common.Interfaces;

public sealed record WorkflowActionPluginContext(
    Guid TenantId,
    Guid WorkflowInstanceId,
    string StepId,
    string TriggerPhase,
    StepActionDefinition Action,
    Dictionary<string, object> RuntimePayload,
    string? ResolvedTarget,
    string? ResolvedCapability,
    string? ResolvedUrl,
    string? ResolvedTemplate,
    Dictionary<string, string>? ResolvedHeaders,
    object ResolvedActionPayload);

public sealed record WorkflowActionPluginResult(
    string MessageType,
    object MessagePayload);

public interface IWorkflowActionPlugin
{
    string ActionType { get; }

    WorkflowActionPluginResult BuildMessage(WorkflowActionPluginContext context);
}

public interface IWorkflowActionPluginRegistry
{
    bool TryResolve(string actionType, out IWorkflowActionPlugin plugin);
}
