using System.Collections.Generic;
using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Infrastructure.Services;

internal static class WorkflowActionPluginPayloadFactory
{
    public static Dictionary<string, object> BuildPayload(WorkflowActionPluginContext context)
    {
        var actionData = new Dictionary<string, object>
        {
            ["tenantId"] = context.TenantId,
            ["workflowInstanceId"] = context.WorkflowInstanceId,
            ["stepId"] = context.StepId,
            ["triggerPhase"] = context.TriggerPhase,
            ["actionType"] = context.Action.ActionType,
            ["target"] = context.ResolvedTarget!,
            ["capability"] = context.ResolvedCapability ?? context.ResolvedTarget!,
            ["url"] = context.ResolvedUrl!,
            ["method"] = context.Action.Method ?? "POST",
            ["template"] = context.ResolvedTemplate!,
            ["signPayload"] = context.Action.SignPayload,
            ["secretName"] = context.Action.SecretName ?? string.Empty,
            ["payload"] = context.ResolvedActionPayload
        };

        if (context.ResolvedHeaders != null)
        {
            actionData["headers"] = context.ResolvedHeaders;
        }

        return actionData;
    }
}
