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
}
