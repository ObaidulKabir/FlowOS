using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Infrastructure.Services;

public sealed class InvokeCapabilityWorkflowActionPlugin : IWorkflowActionPlugin
{
    public string ActionType => "InvokeCapability";

    public WorkflowActionPluginResult BuildMessage(WorkflowActionPluginContext context)
    {
        var payload = WorkflowActionPluginPayloadFactory.BuildPayload(context);
        return new WorkflowActionPluginResult("WorkflowAction:InvokeCapability", payload);
    }
}
