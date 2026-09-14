using System.Collections.Generic;
using System.Linq;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Infrastructure.Services;
using FlowOS.MCP.Tools;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

public class PluginDiscoveryMcpToolsTests
{
    [Fact]
    public async Task ListRegisteredPlugins_ReturnsActionAndDecisionPlugins()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FlowOS:Actions:AllowWildcardPluginFallback"] = "false",
                ["FlowOS:Actions:RejectUnknownActionTypes"] = "true"
            })
            .Build();

        var actionPlugins = new IWorkflowActionPlugin[]
        {
            new WebhookWorkflowActionPlugin(),
            new GenericWorkflowActionPlugin()
        };
        var decisionPlugins = new IPolicyDecisionPlugin[]
        {
            new DefaultPolicyDecisionPlugin()
        };

        var tool = new PluginDiscoveryMcpTools(actionPlugins, decisionPlugins, configuration);
        var result = await tool.ListRegisteredPlugins(new JObject());

        Assert.False(result.IsError);
        var payload = JObject.Parse(result.Content.Single().Text);
        var data = payload["data"] as JObject;
        Assert.NotNull(data);

        Assert.Equal(2, data!["totalActionPlugins"]?.Value<int>());
        Assert.Equal(1, data["totalDecisionPlugins"]?.Value<int>());
        Assert.Contains(
            data["actionPlugins"]!,
            token => string.Equals(token["actionType"]?.ToString(), "Webhook", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            data["decisionPlugins"]!,
            token => string.Equals(token["providerName"]?.ToString(), "default", System.StringComparison.OrdinalIgnoreCase));
        Assert.False(data["runtimePolicy"]?["allowWildcardPluginFallback"]?.Value<bool>());
        Assert.True(data["runtimePolicy"]?["rejectUnknownActionTypes"]?.Value<bool>());
    }

    [Fact]
    public async Task ListRegisteredPlugins_ReturnsEnrichedCapabilityMetadata()
    {
        var configuration = new ConfigurationBuilder().Build();
        var actionPlugins = new IWorkflowActionPlugin[]
        {
            new EmailWorkflowActionPlugin(),
            new SlackWorkflowActionPlugin(),
            new WhatsAppWorkflowActionPlugin(),
            new WebhookWorkflowActionPlugin(),
            new NotificationWorkflowActionPlugin(),
            new PublishEventWorkflowActionPlugin(),
            new InvokeCapabilityWorkflowActionPlugin(),
            new GenericWorkflowActionPlugin()
        };

        var tool = new PluginDiscoveryMcpTools(actionPlugins, Enumerable.Empty<IPolicyDecisionPlugin>(), configuration);
        var result = await tool.ListRegisteredPlugins(new JObject());

        Assert.False(result.IsError);
        var payload = JObject.Parse(result.Content.Single().Text);
        var data = payload["data"] as JObject;
        Assert.NotNull(data);

        var plugins = data!["actionPlugins"] as JArray;
        Assert.NotNull(plugins);

        var email = plugins!.FirstOrDefault(p => string.Equals(p["actionType"]?.ToString(), "Email", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(email);
        Assert.Equal("communication", email!["category"]?.ToString());
        Assert.NotEmpty(email["description"]?.ToString()!);
        var emailParams = email["supportedParameters"] as JArray;
        Assert.NotNull(emailParams);
        Assert.Contains(emailParams!, p => p["name"]?.ToString() == "to" && p["required"]?.Value<bool>() == true);
        Assert.Contains(emailParams!, p => p["name"]?.ToString() == "subject" && p["required"]?.Value<bool>() == true);
        Assert.NotNull(email["examplePayloadMapping"]);

        var slack = plugins.FirstOrDefault(p => string.Equals(p["actionType"]?.ToString(), "Slack", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(slack);
        Assert.Equal("communication", slack!["category"]?.ToString());
        var slackParams = slack["supportedParameters"] as JArray;
        Assert.NotNull(slackParams);
        Assert.Contains(slackParams!, p => p["name"]?.ToString() == "channel");
        Assert.Contains(slackParams!, p => p["name"]?.ToString() == "text");

        var wa = plugins.FirstOrDefault(p => string.Equals(p["actionType"]?.ToString(), "WhatsApp", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(wa);
        Assert.Equal("communication", wa!["category"]?.ToString());
        var waParams = wa["supportedParameters"] as JArray;
        Assert.NotNull(waParams);
        Assert.Contains(waParams!, p => p["name"]?.ToString() == "recipient");

        var webhook = plugins.FirstOrDefault(p => string.Equals(p["actionType"]?.ToString(), "Webhook", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(webhook);
        Assert.Equal("integration", webhook!["category"]?.ToString());
    }

    [Fact]
    public async Task TestActionPlugin_ValidEmail_BuildsOutboxMessageAndReturnsValid()
    {
        var configuration = new ConfigurationBuilder().Build();
        var tool = new PluginDiscoveryMcpTools(new[] { new EmailWorkflowActionPlugin() }, Enumerable.Empty<IPolicyDecisionPlugin>(), configuration);

        var args = new JObject
        {
            ["actionType"] = "Email",
            ["target"] = "dev-alerts@example.com",
            ["template"] = "Deployment Succeeded",
            ["payload"] = JObject.FromObject(new
            {
                body = "Release 2.4 is live.",
                htmlBody = "<p>Release 2.4 is live.</p>",
                cc = "lead@example.com"
            })
        };

        var result = await tool.TestActionPlugin(args);

        Assert.False(result.IsError);
        var json = JObject.Parse(result.Content.Single().Text);
        Assert.True(json["ok"]?.Value<bool>());

        var data = json["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Email", data!["actionType"]?.ToString());
        Assert.Equal("WorkflowAction:Email", data["messageType"]?.ToString());
        Assert.Equal("Valid", data["status"]?.ToString());

        var builtPayload = data["builtPayload"] as JObject;
        Assert.NotNull(builtPayload);
        Assert.Equal("dev-alerts@example.com", builtPayload!["to"]?.ToString());
        Assert.Equal("Deployment Succeeded", builtPayload["subject"]?.ToString());
        Assert.Equal("Release 2.4 is live.", builtPayload["body"]?.ToString());
        Assert.Equal("<p>Release 2.4 is live.</p>", builtPayload["htmlBody"]?.ToString());
        Assert.Equal("lead@example.com", builtPayload["cc"]?.ToString());
    }

    [Fact]
    public async Task TestActionPlugin_ValidSlack_BuildsOutboxMessageAndReturnsValid()
    {
        var configuration = new ConfigurationBuilder().Build();
        var tool = new PluginDiscoveryMcpTools(new[] { new SlackWorkflowActionPlugin() }, Enumerable.Empty<IPolicyDecisionPlugin>(), configuration);

        var args = new JObject
        {
            ["actionType"] = "Slack",
            ["target"] = "#infra-alerts",
            ["template"] = "High CPU alert on worker-01",
            ["url"] = "https://hooks.slack.com/services/T00/B00/XXXX"
        };

        var result = await tool.TestActionPlugin(args);

        Assert.False(result.IsError);
        var json = JObject.Parse(result.Content.Single().Text);
        var data = json["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Slack", data!["actionType"]?.ToString());
        Assert.Equal("WorkflowAction:Slack", data["messageType"]?.ToString());
        Assert.Equal("Valid", data["status"]?.ToString());

        var builtPayload = data["builtPayload"] as JObject;
        Assert.NotNull(builtPayload);
        Assert.Equal("#infra-alerts", builtPayload!["channel"]?.ToString());
        Assert.Equal("High CPU alert on worker-01", builtPayload["text"]?.ToString());
        Assert.Equal("https://hooks.slack.com/services/T00/B00/XXXX", builtPayload["webhookUrl"]?.ToString());
    }

    [Fact]
    public async Task TestActionPlugin_ValidWhatsApp_BuildsOutboxMessageAndReturnsValid()
    {
        var configuration = new ConfigurationBuilder().Build();
        var tool = new PluginDiscoveryMcpTools(new[] { new WhatsAppWorkflowActionPlugin() }, Enumerable.Empty<IPolicyDecisionPlugin>(), configuration);

        var args = new JObject
        {
            ["actionType"] = "WhatsApp",
            ["target"] = "+14155552671",
            ["template"] = "delivery_alert",
            ["payload"] = JObject.FromObject(new
            {
                language = "en_US",
                parameters = new Dictionary<string, string> { ["orderNumber"] = "ORD-9988" }
            })
        };

        var result = await tool.TestActionPlugin(args);

        Assert.False(result.IsError);
        var json = JObject.Parse(result.Content.Single().Text);
        var data = json["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("WhatsApp", data!["actionType"]?.ToString());
        Assert.Equal("WorkflowAction:WhatsApp", data["messageType"]?.ToString());
        Assert.Equal("Valid", data["status"]?.ToString());

        var builtPayload = data["builtPayload"] as JObject;
        Assert.NotNull(builtPayload);
        Assert.Equal("+14155552671", builtPayload!["recipient"]?.ToString());
        Assert.Equal("delivery_alert", builtPayload["templateName"]?.ToString());
        Assert.Equal("en_US", builtPayload["language"]?.ToString());
    }

    [Fact]
    public async Task TestActionPlugin_MissingActionType_ReturnsArgError()
    {
        var configuration = new ConfigurationBuilder().Build();
        var tool = new PluginDiscoveryMcpTools(Enumerable.Empty<IWorkflowActionPlugin>(), Enumerable.Empty<IPolicyDecisionPlugin>(), configuration);

        var result = await tool.TestActionPlugin(new JObject());

        Assert.True(result.IsError);
        var json = JObject.Parse(result.Content.Single().Text);
        Assert.Equal("MCP-ARG-001", json["errorCode"]?.ToString());
    }

    [Fact]
    public async Task TestActionPlugin_UnregisteredPlugin_ReturnsNotFoundError()
    {
        var configuration = new ConfigurationBuilder().Build();
        var tool = new PluginDiscoveryMcpTools(new[] { new EmailWorkflowActionPlugin() }, Enumerable.Empty<IPolicyDecisionPlugin>(), configuration);

        var result = await tool.TestActionPlugin(new JObject { ["actionType"] = "Telegram" });

        Assert.True(result.IsError);
        var json = JObject.Parse(result.Content.Single().Text);
        Assert.Equal("MCP-NOTFOUND-001", json["errorCode"]?.ToString());
    }

    [Fact]
    public async Task TestActionPlugin_IncompleteEmail_ReturnsWarningDiagnostics()
    {
        var configuration = new ConfigurationBuilder().Build();
        var tool = new PluginDiscoveryMcpTools(new[] { new EmailWorkflowActionPlugin() }, Enumerable.Empty<IPolicyDecisionPlugin>(), configuration);

        var args = new JObject
        {
            ["actionType"] = "Email",
            ["target"] = "invalid-address"
        };

        var result = await tool.TestActionPlugin(args);

        Assert.False(result.IsError);
        var json = JObject.Parse(result.Content.Single().Text);
        var data = json["data"] as JObject;
        Assert.NotNull(data);
        Assert.Equal("Warning", data!["status"]?.ToString());

        var diagnostics = data["diagnostics"] as JArray;
        Assert.NotNull(diagnostics);
        Assert.NotEmpty(diagnostics!);
        Assert.Contains(diagnostics!, d => d.ToString().Contains("'@'"));
    }
}
