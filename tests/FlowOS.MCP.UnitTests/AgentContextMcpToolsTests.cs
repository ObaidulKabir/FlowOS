using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

public sealed class AgentContextMcpToolsTests
{
    [Fact]
    public async Task GetAgentContext_ReturnsComposedPromptDataToolsProviderWithoutRunningAgent()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var instanceId = Guid.NewGuid();
            var packet = new DecisionPacket(
                tenantId,
                instanceId,
                "ApproveQuote",
                "Assigned",
                "HumanTask",
                "Either",
                "Accept if within 15%.",
                "Shop overlay.",
                new Dictionary<string, object?> { ["Amount"] = 4800L },
                new[] { "QUOTE_APPROVED" },
                new[] { "QUOTE_APPROVED" },
                new[] { "ServiceAdvisor" },
                Array.Empty<SlaReminderFact>(),
                "QUOTE_RESPONSE_OVERDUE",
                new Dictionary<string, object>(),
                "Decide",
                new AutoCommitPolicy(0.9, new[] { "QUOTE_APPROVED" }),
                new AgentProviderRef("quote-llm", "openai", "gpt-4o-mini", null, true),
                new[]
                {
                    new AgentToolDescriptor(
                        "LookupRecord:crm.customer.get.v1",
                        "resource",
                        "LookupRecord",
                        "lookup",
                        "none",
                        true,
                        "crm.customer.get.v1")
                },
                new AgentPromptRef("quote-approval", "Quote approval", "System", "Approve in-band quotes."));

            var tools = new AgentContextMcpTools(new StubPacketBuilder(packet), new InMemoryPromptRegistry());
            var result = await tools.GetAgentContext(JObject.FromObject(new
            {
                tenantId,
                workflowInstanceId = instanceId,
                prefetch = false
            }));

            Assert.False(result.IsError);
            var json = JObject.Parse(result.Content.Single().Text);
            var context = json["data"]!["agentContext"]!;
            Assert.Equal("live", context["mode"]?.ToString());
            Assert.False(json["data"]!["ranAgent"]?.Value<bool>());
            Assert.Equal("quote-approval", context["prompt"]!["alias"]?.ToString());
            Assert.Equal(4800, context["data"]!["canonicalContext"]!["Amount"]?.Value<long>());
            Assert.Equal("openai", context["provider"]!["providerName"]?.ToString());
            Assert.Null(context["provider"]!["apiKey"]);
            Assert.Contains(context["tools"]!, token => token["name"]?.ToString() == "LookupRecord:crm.customer.get.v1");
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task UpsertAndGetAgentPrompt_RoundTripsInstructions()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var registry = new InMemoryPromptRegistry();
            var tools = new AgentContextMcpTools(new StubPacketBuilder(null), registry);

            var upsert = await tools.UpsertAgentPrompt(JObject.FromObject(new
            {
                tenantId,
                alias = "quote-approval",
                title = "Quote approval",
                system = "Suggest legal events only.",
                instructions = "Approve if within 15% of estimate."
            }));
            Assert.False(upsert.IsError);

            var listed = await tools.ListAgentPrompts(JObject.FromObject(new { tenantId }));
            var listJson = JObject.Parse(listed.Content.Single().Text);
            Assert.Equal(1, listJson["data"]!["totalCount"]?.Value<int>());

            var loaded = await tools.GetAgentPrompt(JObject.FromObject(new { tenantId, alias = "quote-approval" }));
            var prompt = JObject.Parse(loaded.Content.Single().Text)["data"]!;
            Assert.Equal("quote-approval", prompt["alias"]?.ToString());
            Assert.Equal("Approve if within 15% of estimate.", prompt["instructions"]?.ToString());
            Assert.DoesNotContain("apiKey", prompt.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task UpsertAndGetAgentProvider_RedactsApiKey()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var registry = new InMemoryPromptRegistry();
            var tools = new AgentContextMcpTools(new StubPacketBuilder(null), registry);

            var upsert = await tools.UpsertAgentProvider(JObject.FromObject(new
            {
                tenantId,
                alias = "quote-llm",
                providerName = "openai",
                model = "gpt-4o-mini",
                apiKey = "sk-test-do-not-return"
            }));
            Assert.False(upsert.IsError);

            var listed = await tools.ListAgentProviders(JObject.FromObject(new { tenantId }));
            var listJson = JObject.Parse(listed.Content.Single().Text);
            Assert.Equal(1, listJson["data"]!["totalCount"]?.Value<int>());

            var loaded = await tools.GetAgentProvider(JObject.FromObject(new { tenantId, alias = "quote-llm" }));
            var provider = JObject.Parse(loaded.Content.Single().Text)["data"]!;
            Assert.Equal("quote-llm", provider["alias"]?.ToString());
            Assert.Equal("openai", provider["providerName"]?.ToString());
            Assert.Equal("gpt-4o-mini", provider["model"]?.ToString());
            Assert.True(provider["hasApiKey"]?.Value<bool>());
            Assert.Null(provider["apiKey"]);
            Assert.DoesNotContain("sk-test", provider.ToString(), StringComparison.OrdinalIgnoreCase);

            var keepKey = await tools.UpsertAgentProvider(JObject.FromObject(new
            {
                tenantId,
                alias = "quote-llm",
                model = "gpt-4o"
            }));
            Assert.False(keepKey.IsError);
            var updated = JObject.Parse(keepKey.Content.Single().Text)["data"]!;
            Assert.Equal("gpt-4o", updated["model"]?.ToString());
            Assert.True(updated["hasApiKey"]?.Value<bool>());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task PreviewAgentContext_MissingStep_ReturnsNotFound()
    {
        McpRequestContext.Clear();
        try
        {
            var tools = new AgentContextMcpTools(
                new MissingStepPacketBuilder(),
                new InMemoryPromptRegistry());

            var result = await tools.PreviewAgentContext(JObject.FromObject(new
            {
                tenantId = Guid.NewGuid(),
                workflowClassId = Guid.NewGuid(),
                stepId = "ApproveQuote"
            }));

            Assert.True(result.IsError);
            var json = JObject.Parse(result.Content.Single().Text);
            Assert.Equal("MCP-NOTFOUND-001", json["errorCode"]?.ToString());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    private sealed class StubPacketBuilder : IDecisionPacketBuilder
    {
        private readonly DecisionPacket? _packet;

        public StubPacketBuilder(DecisionPacket? packet) => _packet = packet;

        public Task<DecisionPacket?> BuildAsync(
            Guid tenantId,
            Guid workflowInstanceId,
            string? objective = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_packet);

        public Task<DecisionPacket?> PreviewAsync(
            Guid tenantId,
            Guid workflowClassId,
            string stepId,
            Guid? contextBindingId = null,
            IReadOnlyDictionary<string, object?>? canonicalSample = null,
            string? currentState = null,
            string? objective = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_packet);
    }

    private sealed class MissingStepPacketBuilder : IDecisionPacketBuilder
    {
        public Task<DecisionPacket?> BuildAsync(
            Guid tenantId,
            Guid workflowInstanceId,
            string? objective = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DecisionPacket?>(null);

        public Task<DecisionPacket?> PreviewAsync(
            Guid tenantId,
            Guid workflowClassId,
            string stepId,
            Guid? contextBindingId = null,
            IReadOnlyDictionary<string, object?>? canonicalSample = null,
            string? currentState = null,
            string? objective = null,
            CancellationToken cancellationToken = default) =>
            throw new KeyNotFoundException($"Step '{stepId}' was not found on workflow class 'Quote'.");
    }

    private sealed class InMemoryPromptRegistry : IPluginBindingRegistryService
    {
        private readonly Dictionary<string, PluginBindingDto> _items = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _rawJson = new(StringComparer.OrdinalIgnoreCase);

        public Task<PluginBindingDto> UpsertAsync(
            Guid tenantId,
            string bindingType,
            string sourceName,
            string providerName,
            bool isEnabled = true,
            string? configurationJson = null,
            CancellationToken ct = default)
        {
            object? configuration;
            if (string.Equals(bindingType, PluginBindingTypes.Agent, StringComparison.OrdinalIgnoreCase))
            {
                var merged = AgentProviderConfiguration.Merge(
                    _rawJson.GetValueOrDefault(sourceName),
                    configurationJson);
                _rawJson[sourceName] = merged;
                configuration = AgentProviderConfiguration.Redact(merged);
            }
            else
            {
                var existingJson = _rawJson.GetValueOrDefault(sourceName);
                var merged = AgentPromptConfiguration.Merge(existingJson, configurationJson);
                _rawJson[sourceName] = merged;
                configuration = AgentPromptConfiguration.Public(merged);
            }

            if (_items.TryGetValue(sourceName, out var existing))
            {
                var updated = existing with
                {
                    ProviderName = providerName,
                    IsEnabled = isEnabled,
                    Configuration = configuration,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                _items[sourceName] = updated;
                return Task.FromResult(updated);
            }

            var created = new PluginBindingDto(
                Guid.NewGuid(),
                tenantId,
                bindingType,
                sourceName,
                providerName,
                isEnabled,
                DateTime.UtcNow,
                DateTime.UtcNow,
                configuration);
            _items[sourceName] = created;
            return Task.FromResult(created);
        }

        public Task<IReadOnlyList<PluginBindingDto>> ListAsync(
            Guid tenantId,
            string? bindingType = null,
            string? sourceName = null,
            bool? enabledOnly = null,
            CancellationToken ct = default)
        {
            IEnumerable<PluginBindingDto> query = _items.Values.Where(item => item.TenantId == tenantId);
            if (!string.IsNullOrWhiteSpace(bindingType))
                query = query.Where(item => item.BindingType == bindingType);
            if (!string.IsNullOrWhiteSpace(sourceName))
                query = query.Where(item => string.Equals(item.SourceName, sourceName, StringComparison.OrdinalIgnoreCase));
            if (enabledOnly == true)
                query = query.Where(item => item.IsEnabled);
            return Task.FromResult<IReadOnlyList<PluginBindingDto>>(query.ToList());
        }

        public Task<string?> ResolveProviderNameAsync(
            Guid tenantId,
            string bindingType,
            string sourceName,
            CancellationToken ct = default) =>
            Task.FromResult(_items.TryGetValue(sourceName, out var item) ? item.ProviderName : null);

        public Task<Dictionary<string, string>> ResolveBindingsAsync(
            Guid tenantId,
            string bindingType,
            CancellationToken ct = default) =>
            Task.FromResult(_items.ToDictionary(item => item.Key, item => item.Value.ProviderName, StringComparer.OrdinalIgnoreCase));

        public Task<PluginBindingDto?> GetEnabledAsync(
            Guid tenantId,
            string bindingType,
            string sourceName,
            CancellationToken ct = default)
        {
            if (_items.TryGetValue(sourceName, out var item) && item.IsEnabled && item.TenantId == tenantId)
                return Task.FromResult<PluginBindingDto?>(item);
            return Task.FromResult<PluginBindingDto?>(null);
        }

        public Task<AgentProviderConfiguration?> GetAgentSecretsAsync(
            Guid tenantId,
            string sourceName,
            CancellationToken ct = default) =>
            Task.FromResult(AgentProviderConfiguration.Parse(_rawJson.GetValueOrDefault(sourceName)));
    }
}
