using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Infrastructure.Services;

public sealed class WebhookWorkflowActionPlugin : IWorkflowActionPlugin
{
    public string ActionType => "Webhook";

    public WorkflowActionPluginResult BuildMessage(WorkflowActionPluginContext context)
    {
        var payload = WorkflowActionPluginPayloadFactory.BuildPayload(context);
        return new WorkflowActionPluginResult("WorkflowAction:Webhook", payload);
    }
}
