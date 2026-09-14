using System;
using System.Collections.Generic;
using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Infrastructure.Services;

public sealed class WhatsAppWorkflowActionPlugin : IWorkflowActionPlugin
{
    public string ActionType => "WhatsApp";

    public WorkflowActionPluginResult BuildMessage(WorkflowActionPluginContext context)
    {
        var basePayload = WorkflowActionPluginPayloadFactory.BuildPayload(context);

        string? recipient = context.ResolvedTarget;
        string? templateName = null;
        string? message = context.ResolvedTemplate;
        string language = "en_US";
        object? parameters = null;
        string? mediaUrl = null;

        if (context.ResolvedActionPayload is IDictionary<string, object> dict)
        {
            if (dict.TryGetValue("recipient", out var recVal) && recVal != null)
                recipient = recVal.ToString();
            else if (dict.TryGetValue("to", out var toVal) && toVal != null)
                recipient = toVal.ToString();
            else if (dict.TryGetValue("phoneNumber", out var phoneVal) && phoneVal != null)
                recipient = phoneVal.ToString();
            else if (dict.TryGetValue("phone", out var pVal) && pVal != null)
                recipient = pVal.ToString();

            if (dict.TryGetValue("templateName", out var tnVal) && tnVal != null)
                templateName = tnVal.ToString();
            else if (dict.TryGetValue("template", out var tmplVal) && tmplVal != null)
                templateName = tmplVal.ToString();

            if (dict.TryGetValue("message", out var msgVal) && msgVal != null)
                message = msgVal.ToString();
            else if (dict.TryGetValue("body", out var bodyVal) && bodyVal != null)
                message = bodyVal.ToString();

            if (dict.TryGetValue("language", out var langVal) && langVal != null)
                language = langVal.ToString()!;

            if (dict.TryGetValue("parameters", out var paramsVal))
                parameters = paramsVal;
            else if (dict.TryGetValue("components", out var compVal))
                parameters = compVal;

            if (dict.TryGetValue("mediaUrl", out var mediaVal) && mediaVal != null)
                mediaUrl = mediaVal.ToString();
        }

        if (string.IsNullOrWhiteSpace(templateName) && !string.IsNullOrWhiteSpace(context.ResolvedTemplate) && !context.ResolvedTemplate.Contains(' '))
        {
            templateName = context.ResolvedTemplate;
        }

        basePayload["recipient"] = recipient ?? string.Empty;
        basePayload["language"] = language;
        if (!string.IsNullOrEmpty(templateName)) basePayload["templateName"] = templateName;
        if (!string.IsNullOrEmpty(message)) basePayload["message"] = message;
        if (parameters != null) basePayload["parameters"] = parameters;
        if (!string.IsNullOrEmpty(mediaUrl)) basePayload["mediaUrl"] = mediaUrl;

        return new WorkflowActionPluginResult("WorkflowAction:WhatsApp", basePayload);
    }
}
