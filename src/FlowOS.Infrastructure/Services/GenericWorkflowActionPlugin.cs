using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Infrastructure.Services;

// Minimal built-in plugin adapter that preserves current outbox payload shape.
public sealed class GenericWorkflowActionPlugin : IWorkflowActionPlugin
{
    public string ActionType => "*";

    public WorkflowActionPluginResult BuildMessage(WorkflowActionPluginContext context)
    {
        var actionData = WorkflowActionPluginPayloadFactory.BuildPayload(context);

        return new WorkflowActionPluginResult(
            MessageType: $"WorkflowAction:{context.Action.ActionType}",
            MessagePayload: actionData);
    }
}
