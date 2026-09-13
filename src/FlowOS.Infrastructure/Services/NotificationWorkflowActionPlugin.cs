using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Infrastructure.Services;

public sealed class NotificationWorkflowActionPlugin : IWorkflowActionPlugin
{
    public string ActionType => "Notification";

    public WorkflowActionPluginResult BuildMessage(WorkflowActionPluginContext context)
    {
        var payload = WorkflowActionPluginPayloadFactory.BuildPayload(context);
        return new WorkflowActionPluginResult("WorkflowAction:Notification", payload);
    }
}
