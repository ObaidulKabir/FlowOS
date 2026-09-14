using System;
using System.Collections.Generic;
using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Infrastructure.Services;

public sealed class SlackWorkflowActionPlugin : IWorkflowActionPlugin
{
    public string ActionType => "Slack";

    public WorkflowActionPluginResult BuildMessage(WorkflowActionPluginContext context)
    {
        var basePayload = WorkflowActionPluginPayloadFactory.BuildPayload(context);

        string? channel = context.ResolvedTarget;
        string? text = context.ResolvedTemplate;
        string? webhookUrl = context.ResolvedUrl;
        object? blocks = null;
        object? attachments = null;

        if (context.ResolvedActionPayload is IDictionary<string, object> dict)
        {
            if (dict.TryGetValue("channel", out var chVal) && chVal != null)
                channel = chVal.ToString();

            if (dict.TryGetValue("text", out var textVal) && textVal != null)
                text = textVal.ToString();
            else if (dict.TryGetValue("message", out var msgVal) && msgVal != null)
                text = msgVal.ToString();

            if (dict.TryGetValue("webhookUrl", out var whVal) && whVal != null)
                webhookUrl = whVal.ToString();
            else if (dict.TryGetValue("url", out var urlVal) && urlVal != null)
                webhookUrl = urlVal.ToString();

            if (dict.TryGetValue("blocks", out var blocksVal))
                blocks = blocksVal;

            if (dict.TryGetValue("attachments", out var attVal))
                attachments = attVal;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            text = $"Workflow Notification - Step: {context.StepId} (Instance: {context.WorkflowInstanceId})";
        }

        basePayload["channel"] = channel ?? "#general";
        basePayload["text"] = text;
        if (!string.IsNullOrEmpty(webhookUrl)) basePayload["webhookUrl"] = webhookUrl;
        if (blocks != null) basePayload["blocks"] = blocks;
        if (attachments != null) basePayload["attachments"] = attachments;

        return new WorkflowActionPluginResult("WorkflowAction:Slack", basePayload);
    }
}
