using FlowOS.Core.Common.Services;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.MCP.UnitTests;

public sealed class PluginBindingMcpToolsTests
{
    // Tenant integration finding H3 (P0): the alias "flowos-hosted" is platform-reserved - a step
    // whose agentProvider resolves to it always gets the platform's hosted lease at runtime - but
    // register_plugin_binding (bindingType: agent) accepted saving a different (BYO) providerName
    // under that alias with no warning. Must now be rejected at write time, the same as
    // upsert_agent_provider (see AgentContextMcpToolsTests.UpsertAgentProvider_ReservedAliasFlowOsHosted_RejectsNonHostedProviderName).
    [Fact]
    public async Task RegisterPluginBinding_ReservedAliasFlowOsHosted_RejectsNonHostedProviderName()
    {
        McpRequestContext.Clear();
        try
        {
            var tools = new PluginBindingMcpTools(NullPluginBindingRegistryService.Instance);

            var result = await tools.RegisterPluginBinding(JObject.FromObject(new
            {
                tenantId = Guid.NewGuid(),
                bindingType = "agent",
                sourceName = "flowos-hosted",
                providerName = "openai"
            }));

            Assert.True(result.IsError);
            Assert.Contains("AGENT-ALIAS-RESERVED", result.Content.Single().Text);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task RegisterPluginBinding_ReservedAliasFlowOsHosted_AllowsFlowOsHostedProviderName()
    {
        McpRequestContext.Clear();
        try
        {
            // NullPluginBindingRegistryService.UpsertAsync always throws (it's a null-object
            // write sink), so this only proves the reserved-alias guard itself does not reject
            // the legitimate flowos-hosted/flowos-hosted combination - the failure that does
            // occur is the registry's own NotSupportedException mapped to MCP-INTERNAL, past
            // the point where AGENT-ALIAS-RESERVED would have been returned.
            var tools = new PluginBindingMcpTools(NullPluginBindingRegistryService.Instance);

            var result = await tools.RegisterPluginBinding(JObject.FromObject(new
            {
                tenantId = Guid.NewGuid(),
                bindingType = "agent",
                sourceName = "flowos-hosted",
                providerName = "flowos-hosted"
            }));

            Assert.True(result.IsError);
            Assert.DoesNotContain("AGENT-ALIAS-RESERVED", result.Content.Single().Text);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }
}
