using System;
using System.Collections.Generic;
using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Infrastructure.Services;

public sealed class EmailWorkflowActionPlugin : IWorkflowActionPlugin
{
    public string ActionType => "Email";

    public WorkflowActionPluginResult BuildMessage(WorkflowActionPluginContext context)
    {
        var basePayload = WorkflowActionPluginPayloadFactory.BuildPayload(context);

        string? to = context.ResolvedTarget;
        string? subject = null;
        string? body = context.ResolvedTemplate;
        string? htmlBody = null;
        string? cc = null;
        string? bcc = null;

        if (context.ResolvedActionPayload is IDictionary<string, object> dict)
        {
            if (dict.TryGetValue("to", out var toVal) && toVal != null)
                to = toVal.ToString();
            else if (dict.TryGetValue("recipient", out var recVal) && recVal != null)
                to = recVal.ToString();

            if (dict.TryGetValue("subject", out var subjVal) && subjVal != null)
                subject = subjVal.ToString();

            if (dict.TryGetValue("body", out var bodyVal) && bodyVal != null)
                body = bodyVal.ToString();
            else if (dict.TryGetValue("message", out var msgVal) && msgVal != null)
                body = msgVal.ToString();

            if (dict.TryGetValue("html", out var htmlVal) && htmlVal != null)
                htmlBody = htmlVal.ToString();
            else if (dict.TryGetValue("htmlBody", out var htmlBodyVal) && htmlBodyVal != null)
                htmlBody = htmlBodyVal.ToString();

            if (dict.TryGetValue("cc", out var ccVal) && ccVal != null)
                cc = ccVal.ToString();

            if (dict.TryGetValue("bcc", out var bccVal) && bccVal != null)
                bcc = bccVal.ToString();
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            subject = !string.IsNullOrWhiteSpace(context.ResolvedTemplate)
                ? context.ResolvedTemplate
                : $"Workflow Notification - Step: {context.StepId}";
        }

        basePayload["to"] = to ?? string.Empty;
        basePayload["subject"] = subject;
        basePayload["body"] = body ?? string.Empty;
        if (!string.IsNullOrEmpty(htmlBody)) basePayload["htmlBody"] = htmlBody;
        if (!string.IsNullOrEmpty(cc)) basePayload["cc"] = cc;
        if (!string.IsNullOrEmpty(bcc)) basePayload["bcc"] = bcc;

        return new WorkflowActionPluginResult("WorkflowAction:Email", basePayload);
    }
}
