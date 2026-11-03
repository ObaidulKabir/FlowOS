using FlowOS.Core.Common.Interfaces;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using MediatR;
using Moq;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

/// <summary>
/// ADR-007 §9 (amendment): <see cref="AgentWorkflowClassMcpTools.DescribeTenantAgentBindings"/>
/// is advisory discovery only — it must never leak binding secrets, and it must never fail or
/// block just because a tenant has declared nothing yet.
/// </summary>
public class AgentWorkflowClassMcpToolsTests
{
    private static (AgentWorkflowClassMcpTools Tool, Mock<IPluginBindingRegistryService> Registry) BuildTool(Guid tenantId)
    {
        var mediator = new Mock<IMediator>();
        var registry = new Mock<IPluginBindingRegistryService>();

        registry
            .Setup(r => r.ListAgentToolsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PluginBindingDto>
            {
                new(Guid.NewGuid(), tenantId, PluginBindingTypes.Action, "ticket-search", "ZendeskSearch", true,
                    DateTime.UtcNow, DateTime.UtcNow),
            });

        registry
            .Setup(r => r.ListAgentProfilesAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PluginBindingDto>());

        registry
            .Setup(r => r.ListAsync(tenantId, PluginBindingTypes.Agent, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PluginBindingDto>
            {
                new(Guid.NewGuid(), tenantId, PluginBindingTypes.Agent, "default-llm", "anthropic", true,
                    DateTime.UtcNow, DateTime.UtcNow),
            });

        registry
            .Setup(r => r.GetDefaultAgentProviderAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PluginBindingDto?)null);

        return (new AgentWorkflowClassMcpTools(mediator.Object, registry.Object), registry);
    }

    [Fact]
    public async Task DescribeTenantAgentBindings_ReturnsToolsAndProvidersWithoutSecrets()
    {
        var tenantId = Guid.NewGuid();
        var (tool, _) = BuildTool(tenantId);

        McpRequestContext.TenantId = tenantId;
        try
        {
            var result = await tool.DescribeTenantAgentBindings(JObject.FromObject(new { tenantId }));
            var payload = JObject.Parse(result.Content.Single().Text!);

            Assert.False(result.IsError);
            var data = payload["data"] as JObject;
            Assert.NotNull(data);

            var tools = (JArray)data!["tools"]!;
            Assert.Single(tools);
            Assert.Equal("ticket-search", tools[0]["sourceName"]!.Value<string>());
            Assert.Equal("ZendeskSearch", tools[0]["providerName"]!.Value<string>());

            // Never a credential, a configuration blob, or anything beyond the four
            // advisory fields — this tool must not become a secret-exfiltration path.
            Assert.Equal(
                new[] { "sourceName", "providerName", "bindingType", "isEnabled" },
                ((JObject)tools[0]).Properties().Select(p => p.Name).ToArray());

            var providers = (JArray)data["providers"]!;
            Assert.Single(providers);
            Assert.Equal("anthropic", providers[0]["providerName"]!.Value<string>());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task DescribeTenantAgentBindings_NoBindingsDeclared_StillSucceedsWithGuidanceMessage()
    {
        var tenantId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        var registry = new Mock<IPluginBindingRegistryService>();

        registry.Setup(r => r.ListAgentToolsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PluginBindingDto>());
        registry.Setup(r => r.ListAgentProfilesAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PluginBindingDto>());
        registry.Setup(r => r.ListAsync(tenantId, PluginBindingTypes.Agent, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PluginBindingDto>());
        registry.Setup(r => r.GetDefaultAgentProviderAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PluginBindingDto?)null);

        var tool = new AgentWorkflowClassMcpTools(mediator.Object, registry.Object);

        McpRequestContext.TenantId = tenantId;
        try
        {
            var result = await tool.DescribeTenantAgentBindings(JObject.FromObject(new { tenantId }));
            var payload = JObject.Parse(result.Content.Single().Text!);

            Assert.False(result.IsError);
            var data = payload["data"] as JObject;
            Assert.NotNull(data);
            Assert.Empty((JArray)data!["tools"]!);
            Assert.Empty((JArray)data["providers"]!);
            // Declaring nothing is not a failure: an AgentDefinitionBlueprint can still
            // name capability tags this tenant does not yet satisfy (ADR-007 §2, §9).
            Assert.Contains("not-ready", data["message"]!.Value<string>());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }
}
