using FlowOS.Application.Common.Interfaces;
using FlowOS.Agents.Abstractions;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

/// <summary>
/// Applications (3/4): an optional <c>applicationId</c> on the agent-prompt and agent-profile MCP tools.
/// Uses the real <see cref="PluginBindingRegistryService"/> over an in-memory EF database (rather than
/// the file's <c>InMemoryPromptRegistry</c> fake, which is keyed by sourceName alone and cannot hold a
/// tenant-shared and an application-scoped binding with the same alias side by side).
/// </summary>
public sealed class AgentContextMcpToolsApplicationScopeTests
{
    private sealed class NullPacketBuilder : IDecisionPacketBuilder
    {
        public Task<DecisionPacket?> BuildAsync(
            Guid tenantId, Guid workflowInstanceId, string? objective = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<DecisionPacket?> PreviewAsync(
            Guid tenantId, Guid workflowClassId, string stepId, Guid? contextBindingId = null,
            IReadOnlyDictionary<string, object?>? canonicalSample = null, string? currentState = null,
            string? objective = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private static (AgentContextMcpTools Tools, TenantApplicationService Apps) NewTools(Guid tenantId)
    {
        var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"agent-context-app-scope-{Guid.NewGuid()}")
            .Options);
        var registry = new PluginBindingRegistryService(db);
        var apps = new TenantApplicationService(db);
        var tools = new AgentContextMcpTools(new NullPacketBuilder(), registry);
        return (tools, apps);
    }

    [Fact]
    public async Task UpsertAgentPrompt_WithApplicationId_SavesInTheApplication_AndFallsBackToSharedWhenAbsent()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var (tools, apps) = NewTools(tenantId);
            var app = await apps.CreateAsync(tenantId, "Finance Approvals", null);

            await tools.UpsertAgentPrompt(JObject.FromObject(new
            {
                tenantId,
                alias = "quote-approval",
                title = "Shared",
                instructions = "Shared body."
            }));

            var scoped = await tools.UpsertAgentPrompt(JObject.FromObject(new
            {
                tenantId,
                applicationId = app.Id,
                alias = "quote-approval",
                title = "App-specific",
                instructions = "App body."
            }));
            Assert.False(scoped.IsError);
            var scopedDto = JObject.Parse(scoped.Content.Single().Text)["data"]!;
            Assert.Equal(app.Id.ToString(), scopedDto["applicationId"]?.ToString());

            // get_agent_prompt with applicationId: the application's own prompt wins.
            var resolvedForApp = await tools.GetAgentPrompt(JObject.FromObject(
                new { tenantId, applicationId = app.Id, alias = "quote-approval" }));
            Assert.Equal("App-specific", JObject.Parse(resolvedForApp.Content.Single().Text)["data"]!["title"]?.ToString());

            // get_agent_prompt without applicationId: only the tenant-shared prompt.
            var resolvedTenantOnly = await tools.GetAgentPrompt(JObject.FromObject(
                new { tenantId, alias = "quote-approval" }));
            Assert.Equal("Shared", JObject.Parse(resolvedTenantOnly.Content.Single().Text)["data"]!["title"]?.ToString());

            // A different application with no prompt of its own falls back to the tenant-shared one.
            var otherApp = await apps.CreateAsync(tenantId, "Other App", null);
            var resolvedForOtherApp = await tools.GetAgentPrompt(JObject.FromObject(
                new { tenantId, applicationId = otherApp.Id, alias = "quote-approval" }));
            Assert.Equal("Shared", JObject.Parse(resolvedForOtherApp.Content.Single().Text)["data"]!["title"]?.ToString());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task ListAgentPrompts_FiltersByApplicationIdOrSharedFlag()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var (tools, apps) = NewTools(tenantId);
            var app = await apps.CreateAsync(tenantId, "Finance Approvals", null);

            await tools.UpsertAgentPrompt(JObject.FromObject(
                new { tenantId, alias = "shared-prompt", title = "Shared", instructions = "x" }));
            await tools.UpsertAgentPrompt(JObject.FromObject(
                new { tenantId, applicationId = app.Id, alias = "app-prompt", title = "App", instructions = "y" }));

            var all = JObject.Parse((await tools.ListAgentPrompts(JObject.FromObject(new { tenantId })))
                .Content.Single().Text)["data"]!;
            Assert.Equal(2, all["totalCount"]?.Value<int>());

            var onlyApp = JObject.Parse((await tools.ListAgentPrompts(JObject.FromObject(
                new { tenantId, applicationId = app.Id }))).Content.Single().Text)["data"]!;
            Assert.Equal(1, onlyApp["totalCount"]?.Value<int>());
            Assert.Equal("app-prompt", onlyApp["prompts"]![0]!["alias"]?.ToString());

            var onlyShared = JObject.Parse((await tools.ListAgentPrompts(JObject.FromObject(
                new { tenantId, shared = true }))).Content.Single().Text)["data"]!;
            Assert.Equal(1, onlyShared["totalCount"]?.Value<int>());
            Assert.Equal("shared-prompt", onlyShared["prompts"]![0]!["alias"]?.ToString());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task UpsertAgentProfile_WithApplicationId_ResolvesByAliasOrRoleInThatApplicationFirst()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var (tools, apps) = NewTools(tenantId);
            var app = await apps.CreateAsync(tenantId, "Finance Approvals", null);

            await tools.UpsertAgentProfile(JObject.FromObject(new
            {
                tenantId,
                alias = "CreditUnderwriter",
                role = "LoanOfficer",
                providerAlias = "tenant-shared-provider"
            }));
            await tools.UpsertAgentProfile(JObject.FromObject(new
            {
                tenantId,
                applicationId = app.Id,
                alias = "CreditUnderwriter",
                role = "LoanOfficer",
                providerAlias = "app-provider"
            }));

            var byAlias = await tools.GetAgentProfile(JObject.FromObject(
                new { tenantId, applicationId = app.Id, alias = "CreditUnderwriter" }));
            var byAliasDto = JObject.Parse(byAlias.Content.Single().Text)["data"]!;
            Assert.Equal("app-provider", byAliasDto["providerAlias"]?.ToString());
            Assert.Equal(app.Id.ToString(), byAliasDto["applicationId"]?.ToString());

            var byRole = await tools.GetAgentProfile(JObject.FromObject(
                new { tenantId, applicationId = app.Id, alias = "LoanOfficer" }));
            Assert.Equal("app-provider", JObject.Parse(byRole.Content.Single().Text)["data"]!["providerAlias"]?.ToString());

            var tenantOnly = await tools.GetAgentProfile(JObject.FromObject(
                new { tenantId, alias = "CreditUnderwriter" }));
            Assert.Equal("tenant-shared-provider", JObject.Parse(tenantOnly.Content.Single().Text)["data"]!["providerAlias"]?.ToString());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task UpsertAgentPrompt_InvalidApplicationId_FailsWithTypedError()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var (tools, _) = NewTools(tenantId);

            var result = await tools.UpsertAgentPrompt(JObject.FromObject(new
            {
                tenantId,
                applicationId = "not-a-guid",
                alias = "quote-approval",
                instructions = "x"
            }));

            Assert.True(result.IsError);
            Assert.Contains("MCP-ARG-002", result.Content.Single().Text);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }
}
