using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Services;
using FlowOS.Core.Interfaces;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using FlowOS.Security.Interfaces;
using Microsoft.Extensions.Configuration;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.MCP.UnitTests;

public sealed class PluginBindingMcpToolsTests
{
    /// <summary>
    /// Builds a <see cref="PluginBindingMcpTools"/> with a real <see cref="ICurrentUser"/>/
    /// <see cref="ICapabilityService"/> pair, so the finding #1 capability gate on the "agent"
    /// bindingType path of <see cref="PluginBindingMcpTools.RegisterPluginBinding"/> actually runs -
    /// unlike the plain 1-arg constructions used by the pre-existing tests in this file, which skip
    /// the gate entirely (production DI always supplies both).
    /// </summary>
    private static (PluginBindingMcpTools Tools, Mock<ICapabilityService> Capabilities) CreateGatedTools(
        Guid tenantId,
        IConfiguration? configuration = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.TenantId).Returns(tenantId);
        currentUser.SetupGet(u => u.Roles).Returns(new List<string> { "Admin" });
        currentUser.SetupGet(u => u.Scopes).Returns(Array.Empty<string>());
        currentUser.SetupGet(u => u.IsApiKey).Returns(false);

        var capabilities = new Mock<ICapabilityService>();
        var tools = new PluginBindingMcpTools(
            NullPluginBindingRegistryService.Instance,
            configuration: configuration,
            currentUser: currentUser.Object,
            capabilityService: capabilities.Object);

        return (tools, capabilities);
    }

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

    [Fact]
    public async Task RegisterPluginBinding_AgentType_CallerLacksManageCapability_ReturnsAuthzError()
    {
        // Tenant integration finding #1: register_plugin_binding's "agent" bindingType path had no
        // capability check at all, same gap as upsert_agent_provider.
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var (tools, capabilities) = CreateGatedTools(tenantId);
            capabilities.Setup(c => c.GetCapabilitiesAsync(tenantId, It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new HashSet<string>());

            var result = await tools.RegisterPluginBinding(JObject.FromObject(new
            {
                tenantId,
                bindingType = "agent",
                sourceName = "quote-llm",
                providerName = "openai"
            }));

            Assert.True(result.IsError);
            Assert.Contains("MCP-AUTHZ-001", result.Content.Single().Text);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task RegisterPluginBinding_AgentType_CallerHasManageCapability_PassesGate()
    {
        McpRequestContext.Clear();
        try
        {
            // As in RegisterPluginBinding_ReservedAliasFlowOsHosted_AllowsFlowOsHostedProviderName,
            // NullPluginBindingRegistryService.UpsertAsync always throws, so this only proves the
            // capability gate itself does not reject a caller who holds agent.manage - the failure
            // that does occur is the registry's own NotSupportedException mapped to MCP-INTERNAL,
            // past the point where MCP-AUTHZ-001 would have been returned.
            var tenantId = Guid.NewGuid();
            var (tools, capabilities) = CreateGatedTools(tenantId);
            capabilities.Setup(c => c.GetCapabilitiesAsync(tenantId, It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new HashSet<string> { AgentProviderKinds.ManageCapability });

            var result = await tools.RegisterPluginBinding(JObject.FromObject(new
            {
                tenantId,
                bindingType = "agent",
                sourceName = "quote-llm",
                providerName = "openai"
            }));

            Assert.True(result.IsError);
            Assert.DoesNotContain("MCP-AUTHZ-001", result.Content.Single().Text);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task RegisterPluginBinding_AgentType_NonUrlEndpoint_IsRejected()
    {
        // Tenant integration finding: register_plugin_binding never validated configuration.endpoint
        // at all (unlike upsert_agent_provider, which already rejected non-URL endpoints).
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var (tools, capabilities) = CreateGatedTools(tenantId);
            capabilities.Setup(c => c.GetCapabilitiesAsync(tenantId, It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new HashSet<string> { AgentProviderKinds.ManageCapability });

            var args = new JObject
            {
                ["tenantId"] = tenantId.ToString(),
                ["bindingType"] = "agent",
                ["sourceName"] = "quote-llm",
                ["providerName"] = "openai",
                ["configuration"] = new JObject { ["endpoint"] = "prospectbd2024@gmail.com" }
            };
            var result = await tools.RegisterPluginBinding(args);

            Assert.True(result.IsError);
            Assert.Contains("configuration.endpoint must be a full http(s) URL", result.Content.Single().Text);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task RegisterPluginBinding_AgentType_PlainHttpEndpointOutsideDevelopment_IsRejected()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Production"
                })
                .Build();
            var (tools, capabilities) = CreateGatedTools(tenantId, configuration);
            capabilities.Setup(c => c.GetCapabilitiesAsync(tenantId, It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new HashSet<string> { AgentProviderKinds.ManageCapability });

            var args = new JObject
            {
                ["tenantId"] = tenantId.ToString(),
                ["bindingType"] = "agent",
                ["sourceName"] = "quote-llm",
                ["providerName"] = "openai",
                ["configuration"] = new JObject { ["endpoint"] = "http://api.example.com/v1/chat/completions" }
            };
            var result = await tools.RegisterPluginBinding(args);

            Assert.True(result.IsError);
            Assert.Contains("https://", result.Content.Single().Text);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }
}
