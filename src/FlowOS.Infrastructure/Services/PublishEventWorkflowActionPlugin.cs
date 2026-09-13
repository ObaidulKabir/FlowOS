using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Infrastructure.Services;

public sealed class PublishEventWorkflowActionPlugin : IWorkflowActionPlugin
{
    public string ActionType => "PublishEvent";

    public WorkflowActionPluginResult BuildMessage(WorkflowActionPluginContext context)
    {
        var payload = WorkflowActionPluginPayloadFactory.BuildPayload(context);
        return new WorkflowActionPluginResult("WorkflowAction:PublishEvent", payload);
    }
}
