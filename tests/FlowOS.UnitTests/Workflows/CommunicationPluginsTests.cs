using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.Infrastructure.Services.Communication;
using FlowOS.MCP.Tools;
using FlowOS.Workflows.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class CommunicationPluginsTests
{
    [Fact]
    public void EmailWorkflowActionPlugin_BuildsStructuredOutboxMessage()
    {
        var plugin = new EmailWorkflowActionPlugin();
        Assert.Equal("Email", plugin.ActionType);

        var action = new StepActionDefinition("Email")
        {
            Target = "finance@example.com",
            Template = "Monthly Budget Report",
            PayloadMapping = new Dictionary<string, string>
            {
                { "to", "RecipientEmail" },
                { "subject", "'Budget Approved for ' + Department" },
                { "body", "'Approved amount: $' + Amount" },
                { "htmlBody", "'<h1>Approved</h1><p>Amount: $' + Amount + '</p>'" },
                { "cc", "'auditor@example.com'" }
            }
        };

        var runtimePayload = new Dictionary<string, object>
        {
            ["RecipientEmail"] = "manager@example.com",
            ["Department"] = "Engineering",
            ["Amount"] = 50000
        };

        var resolvedActionPayload = new Dictionary<string, object>
        {
            ["to"] = "manager@example.com",
            ["subject"] = "Budget Approved for Engineering",
            ["body"] = "Approved amount: $50000",
            ["htmlBody"] = "<h1>Approved</h1><p>Amount: $50000</p>",
            ["cc"] = "auditor@example.com"
        };

        var context = new WorkflowActionPluginContext(
            TenantId: Guid.NewGuid(),
            WorkflowInstanceId: Guid.NewGuid(),
            StepId: "ApprovalStep",
            TriggerPhase: "OnExit",
            Action: action,
            RuntimePayload: runtimePayload,
            ResolvedTarget: "manager@example.com",
            ResolvedCapability: null,
            ResolvedUrl: null,
            ResolvedTemplate: "Budget Approved for Engineering",
            ResolvedHeaders: null,
            ResolvedActionPayload: resolvedActionPayload);

        var result = plugin.BuildMessage(context);

        Assert.Equal("WorkflowAction:Email", result.MessageType);
        var payloadDict = result.MessagePayload as Dictionary<string, object>;
        Assert.NotNull(payloadDict);
        Assert.Equal("manager@example.com", payloadDict["to"]);
        Assert.Equal("Budget Approved for Engineering", payloadDict["subject"]);
        Assert.Equal("Approved amount: $50000", payloadDict["body"]);
        Assert.Equal("<h1>Approved</h1><p>Amount: $50000</p>", payloadDict["htmlBody"]);
        Assert.Equal("auditor@example.com", payloadDict["cc"]);
    }

    [Fact]
    public void SlackWorkflowActionPlugin_BuildsStructuredOutboxMessage()
    {
        var plugin = new SlackWorkflowActionPlugin();
        Assert.Equal("Slack", plugin.ActionType);

        var action = new StepActionDefinition("Slack")
        {
            Target = "#deployments",
            Url = "https://hooks.slack.com/services/T00/B00/XXXX",
            Template = "Deployment succeeded for version {{Version}}"
        };

        var resolvedActionPayload = new Dictionary<string, object>
        {
            ["channel"] = "#releases",
            ["text"] = "Deployment v2.4.0 live in production",
            ["blocks"] = new[] { new { type = "section", text = new { type = "mrkdwn", text = "*Deploy Success*" } } }
        };

        var context = new WorkflowActionPluginContext(
            TenantId: Guid.NewGuid(),
            WorkflowInstanceId: Guid.NewGuid(),
            StepId: "DeployStep",
            TriggerPhase: "OnExit",
            Action: action,
            RuntimePayload: new Dictionary<string, object>(),
            ResolvedTarget: "#releases",
            ResolvedCapability: null,
            ResolvedUrl: "https://hooks.slack.com/services/T00/B00/XXXX",
            ResolvedTemplate: "Deployment v2.4.0 live in production",
            ResolvedHeaders: null,
            ResolvedActionPayload: resolvedActionPayload);

        var result = plugin.BuildMessage(context);

        Assert.Equal("WorkflowAction:Slack", result.MessageType);
        var payloadDict = result.MessagePayload as Dictionary<string, object>;
        Assert.NotNull(payloadDict);
        Assert.Equal("#releases", payloadDict["channel"]);
        Assert.Equal("Deployment v2.4.0 live in production", payloadDict["text"]);
        Assert.Equal("https://hooks.slack.com/services/T00/B00/XXXX", payloadDict["webhookUrl"]);
        Assert.NotNull(payloadDict["blocks"]);
    }

    [Fact]
    public void WhatsAppWorkflowActionPlugin_BuildsStructuredOutboxMessage()
    {
        var plugin = new WhatsAppWorkflowActionPlugin();
        Assert.Equal("WhatsApp", plugin.ActionType);

        var action = new StepActionDefinition("WhatsApp")
        {
            Target = "+14155552671",
            Template = "order_status_update"
        };

        var resolvedActionPayload = new Dictionary<string, object>
        {
            ["recipient"] = "+14155552671",
            ["templateName"] = "order_shipped_v2",
            ["language"] = "en_US",
            ["parameters"] = new Dictionary<string, string> { { "order_id", "ORD-1234" } }
        };

        var context = new WorkflowActionPluginContext(
            TenantId: Guid.NewGuid(),
            WorkflowInstanceId: Guid.NewGuid(),
            StepId: "NotifyCustomer",
            TriggerPhase: "OnEntry",
            Action: action,
            RuntimePayload: new Dictionary<string, object>(),
            ResolvedTarget: "+14155552671",
            ResolvedCapability: null,
            ResolvedUrl: null,
            ResolvedTemplate: "order_status_update",
            ResolvedHeaders: null,
            ResolvedActionPayload: resolvedActionPayload);

        var result = plugin.BuildMessage(context);

        Assert.Equal("WorkflowAction:WhatsApp", result.MessageType);
        var payloadDict = result.MessagePayload as Dictionary<string, object>;
        Assert.NotNull(payloadDict);
        Assert.Equal("+14155552671", payloadDict["recipient"]);
        Assert.Equal("order_shipped_v2", payloadDict["templateName"]);
        Assert.Equal("en_US", payloadDict["language"]);
        Assert.NotNull(payloadDict["parameters"]);
    }

    [Fact]
    public async Task WorkflowActionDispatcher_QueuesOutboxMessagesForCommunicationPlugins()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FlowOSDbContext(options);
        var registry = new WorkflowActionPluginRegistry(new IWorkflowActionPlugin[]
        {
            new EmailWorkflowActionPlugin(),
            new SlackWorkflowActionPlugin(),
            new WhatsAppWorkflowActionPlugin()
        });

        var dispatcher = new WorkflowActionDispatcher(db, registry);

        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        var actions = new List<StepActionDefinition>
        {
            new("Email")
            {
                Target = "lead@enterprise.com",
                Template = "Welcome to FlowOS",
                PayloadMapping = new Dictionary<string, string>
                {
                    { "to", "LeadEmail" },
                    { "subject", "'Welcome ' + LeadName" }
                }
            },
            new("Slack")
            {
                Target = "#sales-leads",
                Template = "New lead registered: {{LeadName}}"
            },
            new("WhatsApp")
            {
                Target = "+15551234567",
                Template = "onboarding_alert"
            }
        };

        var payload = new Dictionary<string, object>
        {
            ["LeadEmail"] = "lead@enterprise.com",
            ["LeadName"] = "Alice"
        };

        await dispatcher.QueueActionsAsync(
            tenantId,
            instanceId,
            "OnboardLead",
            "OnExit",
            actions,
            payload,
            CancellationToken.None);

        await db.SaveChangesAsync();

        var messages = await db.OutboxMessages.ToListAsync();
        Assert.Equal(3, messages.Count);

        var emailMsg = messages.FirstOrDefault(m => m.Type == "WorkflowAction:Email");
        Assert.NotNull(emailMsg);
        Assert.Contains("lead@enterprise.com", emailMsg.Payload);

        var slackMsg = messages.FirstOrDefault(m => m.Type == "WorkflowAction:Slack");
        Assert.NotNull(slackMsg);
        Assert.Contains("#sales-leads", slackMsg.Payload);

        var waMsg = messages.FirstOrDefault(m => m.Type == "WorkflowAction:WhatsApp");
        Assert.NotNull(waMsg);
        using var waDoc = JsonDocument.Parse(waMsg.Payload);
        Assert.Equal("+15551234567", waDoc.RootElement.GetProperty("recipient").GetString());
    }

    [Fact]
    public async Task CommunicationSenders_SimulateAndSendSuccessfully()
    {
        var config = new ConfigurationBuilder().Build();

        var emailSender = new DefaultEmailSender(config, NullLogger<DefaultEmailSender>.Instance);
        var emailRes = await emailSender.SendEmailAsync(new EmailSendRequest(
            TenantId: Guid.NewGuid(),
            To: "customer@domain.com",
            Subject: "Test Email",
            Body: "Hello world"));

        Assert.True(emailRes.Success);
        Assert.NotNull(emailRes.MessageId);
        Assert.Equal(200, emailRes.StatusCode);

        var slackSender = new DefaultSlackSender(config, NullLogger<DefaultSlackSender>.Instance);
        var slackRes = await slackSender.SendSlackMessageAsync(new SlackSendRequest(
            TenantId: Guid.NewGuid(),
            Channel: "#alerts",
            Text: "Critical system event"));

        Assert.True(slackRes.Success);
        Assert.NotNull(slackRes.MessageTs);
        Assert.Equal(200, slackRes.StatusCode);

        var waSender = new DefaultWhatsAppSender(config, NullLogger<DefaultWhatsAppSender>.Instance);
        var waRes = await waSender.SendWhatsAppMessageAsync(new WhatsAppSendRequest(
            TenantId: Guid.NewGuid(),
            Recipient: "+1234567890",
            TemplateName: "alert_template"));

        Assert.True(waRes.Success);
        Assert.NotNull(waRes.MessageSid);
        Assert.Equal(200, waRes.StatusCode);
    }

    [Fact]
    public async Task MCP_PluginDiscovery_ReportsEmailSlackWhatsAppAsBuiltIn()
    {
        var actionPlugins = new IWorkflowActionPlugin[]
        {
            new WebhookWorkflowActionPlugin(),
            new NotificationWorkflowActionPlugin(),
            new PublishEventWorkflowActionPlugin(),
            new InvokeCapabilityWorkflowActionPlugin(),
            new EmailWorkflowActionPlugin(),
            new SlackWorkflowActionPlugin(),
            new WhatsAppWorkflowActionPlugin(),
            new GenericWorkflowActionPlugin()
        };

        var config = new ConfigurationBuilder().Build();
        var discoveryTools = new PluginDiscoveryMcpTools(actionPlugins, Enumerable.Empty<IPolicyDecisionPlugin>(), config);

        var result = await discoveryTools.ListRegisteredPlugins(new JObject());
        Assert.False(result.IsError);

        var json = JObject.Parse(result.Content[0].Text);
        Assert.True(json["ok"]?.Value<bool>());
        var data = json["data"] as JObject;
        Assert.NotNull(data);

        var pluginsArray = data["actionPlugins"] as JArray;
        Assert.NotNull(pluginsArray);

        var emailEntry = pluginsArray.FirstOrDefault(p => p["actionType"]?.ToString() == "Email");
        Assert.NotNull(emailEntry);
        Assert.Equal("built-in", emailEntry["kind"]?.ToString());

        var slackEntry = pluginsArray.FirstOrDefault(p => p["actionType"]?.ToString() == "Slack");
        Assert.NotNull(slackEntry);
        Assert.Equal("built-in", slackEntry["kind"]?.ToString());

        var waEntry = pluginsArray.FirstOrDefault(p => p["actionType"]?.ToString() == "WhatsApp");
        Assert.NotNull(waEntry);
        Assert.Equal("built-in", waEntry["kind"]?.ToString());
    }
}
