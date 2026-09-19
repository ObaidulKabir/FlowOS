using FlowOS.Agents.Abstractions;
using FlowOS.API.Services;
using FlowOS.Application.DTOs;
using FlowOS.Application.Services;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using FlowOS.Workflows.Engine;
using Microsoft.EntityFrameworkCore;
using Moq;
using Newtonsoft.Json.Linq;
using FlowOS.StateMachines.Engine;

namespace FlowOS.UnitTests.Agents;

/// <summary>
/// Locks QuoteAutoReview context binding → Agent Context:
/// policyGuideline overlay, preview_agent_context, and bound simulation.
/// </summary>
public class QuoteAgentContextBindingTests
{
    [Fact]
    public async Task Preview_WithQuoteBinding_ComposesPolicyCanonicalAndAutoCommit()
    {
        var (tenantId, db, pack, binding) = await SeedQuoteAsync();
        await using (db)
        {
            var builder = new DecisionPacketBuilder(new UnitOfWork(db), new QuotePromptRegistry(tenantId));
            var packet = await builder.PreviewAsync(
                tenantId,
                pack.Id,
                "AgentReview",
                binding.Id,
                new Dictionary<string, object?>
                {
                    ["QuoteId"] = "Q-77",
                    ["Amount"] = 900L,
                    ["Estimate"] = 880L,
                    ["ApprovalLimit"] = 1500L
                },
                "AgentQueued",
                "Decide the next legal quote event");

            Assert.NotNull(packet);
            Assert.Equal(StepActor.Agent, packet!.Actor);
            Assert.Equal("AgentReview", packet.CurrentStepId);
            Assert.Equal("AgentQueued", packet.CurrentState);
            Assert.Contains("15%", packet.TemplateGuideline);
            Assert.Equal(DataSeeder.QuoteAutoReviewPolicyGuideline, packet.PolicyGuideline);
            Assert.Equal(900L, packet.CanonicalContext["Amount"]);
            Assert.Contains("EVT-ACCEPT", packet.LegalNextStepEvents);
            Assert.Contains("EVT-ACCEPT", packet.AutoCommit!.AllowedEvents);
            Assert.Equal("quote-approval", packet.PromptBinding!.Alias);
            Assert.Equal("Accept in-bound quotes only.", packet.Prompt.Instructions);

            var composed = AgentContextComposer.FromPacket(packet, "preview", pack.Id, hideInstance: true);
            Assert.Equal("preview", composed.mode);
            Assert.Equal(StepActor.Agent, composed.actor);
            Assert.Null(composed.workflowInstanceId);
            Assert.Equal(DataSeeder.QuoteAutoReviewPolicyGuideline, composed.prompt.policyGuideline);
            Assert.Equal("quote-approval", composed.prompt.alias);
            Assert.Equal(900L, composed.data.canonicalContext["Amount"]);
            Assert.Contains(composed.tools, tool => tool.kind == "event" && tool.name == "EVT-ACCEPT");
            Assert.Null(composed.provider);
            Assert.True(composed.resolved.prompt);
            Assert.False(composed.resolved.provider);
            Assert.DoesNotContain("apiKey", System.Text.Json.JsonSerializer.Serialize(composed), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Preview_WithoutBinding_KeepsTemplateAndOmitsPolicy()
    {
        var (tenantId, db, pack, _) = await SeedQuoteAsync();
        await using (db)
        {
            var packet = await new DecisionPacketBuilder(new UnitOfWork(db))
                .PreviewAsync(tenantId, pack.Id, "AgentReview");

            Assert.NotNull(packet);
            Assert.Equal(StepActor.Agent, packet!.Actor);
            Assert.Contains("15%", packet.TemplateGuideline);
            Assert.Null(packet.PolicyGuideline);
        }
    }

    [Fact]
    public async Task Preview_UnknownBinding_ReturnsNull()
    {
        var (tenantId, db, pack, _) = await SeedQuoteAsync();
        await using (db)
        {
            var packet = await new DecisionPacketBuilder(new UnitOfWork(db))
                .PreviewAsync(tenantId, pack.Id, "AgentReview", Guid.NewGuid());
            Assert.Null(packet);
        }
    }

    [Fact]
    public async Task PreviewAgentContext_McpTool_ReturnsBindingOverlayWithoutRunningAgent()
    {
        var (tenantId, db, pack, binding) = await SeedQuoteAsync();
        await using (db)
        {
            McpRequestContext.Clear();
            try
            {
                var tools = new AgentContextMcpTools(
                    new DecisionPacketBuilder(new UnitOfWork(db), new QuotePromptRegistry(tenantId)),
                    new QuotePromptRegistry(tenantId));

                var result = await tools.PreviewAgentContext(JObject.FromObject(new
                {
                    tenantId,
                    workflowClassId = pack.Id,
                    stepId = "AgentReview",
                    contextBindingId = binding.Id,
                    currentState = "AgentQueued",
                    canonicalContext = new
                    {
                        QuoteId = "Q-77",
                        Amount = 900,
                        Estimate = 880
                    },
                    prefetch = false
                }));

                Assert.False(result.IsError);
                var json = JObject.Parse(result.Content.Single().Text);
                Assert.True(json["ok"]?.Value<bool>());
                Assert.False(json["data"]!["ranAgent"]?.Value<bool>());
                var context = json["data"]!["agentContext"]!;
                Assert.Equal("preview", context["mode"]?.ToString());
                Assert.Equal(StepActor.Agent, context["actor"]?.ToString());
                Assert.Equal("AgentReview", context["currentStepId"]?.ToString());
                Assert.Equal(DataSeeder.QuoteAutoReviewPolicyGuideline, context["prompt"]!["policyGuideline"]?.ToString());
                Assert.Equal("quote-approval", context["prompt"]!["alias"]?.ToString());
                Assert.Equal(900, context["data"]!["canonicalContext"]!["Amount"]?.Value<long>());
                Assert.True(context["provider"] == null || context["provider"]!.Type == JTokenType.Null);
            }
            finally
            {
                McpRequestContext.Clear();
            }
        }
    }

    [Fact]
    public async Task SimulateContextBinding_SubmitInBoundQuote_ProjectsCanonicalAndWaitsOnAgentReview()
    {
        var (tenantId, db, _, binding) = await SeedQuoteAsync();
        await using (db)
        {
            var unitOfWork = new UnitOfWork(db);
            var result = await new WorkflowContextSimulationService(
                    unitOfWork,
                    new WorkflowContextBindingValidator(unitOfWork, new Mock<IPolicyDecisionPluginRegistry>().Object),
                    new WorkflowExecutionContextService(unitOfWork),
                    new WorkflowEngine(new StateMachineEngine()))
                .SimulateAsync(tenantId, new WorkflowContextSimulationRequest(
                    ContextBindingId: binding.Id,
                    Revision: "active",
                    InitialPayload: new { quoteId = "Q-77", amount = 900, estimate = 880 },
                    Roles: ["Submitter"],
                    Events: [new WorkflowContextSimulationEventRequest("EVT-SUBMIT")]));

            Assert.Equal("Running", result.Status);
            Assert.Equal("AgentReview", result.CurrentStepId);
            Assert.Equal("AgentQueued", result.CurrentState);
            Assert.Equal(900L, result.FinalCanonicalContext["Amount"]);
            Assert.Equal("Q-77", result.FinalCanonicalContext["QuoteId"]?.ToString());
            Assert.Contains(result.PendingWork, work =>
                work.Kind == "HumanTask" && work.StepId == "AgentReview");
            Assert.True(result.SideEffectsSuppressed);
            Assert.Empty(db.WorkflowInstances);
        }
    }

    private static async Task<(Guid TenantId, FlowOSDbContext Db, FlowOS.Domain.Entities.WorkflowClass Pack, FlowOS.Domain.Entities.WorkflowContextBinding Binding)> SeedQuoteAsync()
    {
        var tenantId = Guid.NewGuid();
        var db = new FlowOSDbContext(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"quote-agent-ctx-{tenantId}")
            .Options);

        await DataSeeder.SeedFlagshipWorkflowsAsync(db, tenantId);
        await DataSeeder.SeedSampleBusinessContextsAsync(db, tenantId);

        var pack = await db.WorkflowClasses.SingleAsync(item =>
            item.TenantId == tenantId && item.Name == "QuoteAutoReview");
        var binding = await db.WorkflowContextBindings.SingleAsync(item =>
            item.TenantId == tenantId && item.Name == "Quote Auto Review Context");
        return (tenantId, db, pack, binding);
    }

    private sealed class QuotePromptRegistry : IPluginBindingRegistryService
    {
        private readonly PluginBindingDto _prompt;

        public QuotePromptRegistry(Guid tenantId)
        {
            _prompt = new PluginBindingDto(
                Guid.NewGuid(),
                tenantId,
                PluginBindingTypes.Prompt,
                "quote-approval",
                AgentPromptKinds.Markdown,
                true,
                DateTime.UtcNow,
                DateTime.UtcNow,
                new AgentPromptConfiguration
                {
                    Title = "Quote approval",
                    System = "Suggest legal nextSteps only.",
                    Instructions = "Accept in-bound quotes only."
                });
        }

        public Task<PluginBindingDto> UpsertAsync(
            Guid tenantId,
            string bindingType,
            string sourceName,
            string providerName,
            bool isEnabled = true,
            string? configurationJson = null,
            CancellationToken ct = default) =>
            Task.FromResult(_prompt);

        public Task<IReadOnlyList<PluginBindingDto>> ListAsync(
            Guid tenantId,
            string? bindingType = null,
            string? sourceName = null,
            bool? enabledOnly = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PluginBindingDto>>([_prompt]);

        public Task<string?> ResolveProviderNameAsync(
            Guid tenantId,
            string bindingType,
            string sourceName,
            CancellationToken ct = default) =>
            Task.FromResult<string?>(_prompt.ProviderName);

        public Task<Dictionary<string, string>> ResolveBindingsAsync(
            Guid tenantId,
            string bindingType,
            CancellationToken ct = default) =>
            Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        public Task<PluginBindingDto?> GetEnabledAsync(
            Guid tenantId,
            string bindingType,
            string sourceName,
            CancellationToken ct = default)
        {
            if (tenantId == _prompt.TenantId &&
                string.Equals(bindingType, PluginBindingTypes.Prompt, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(sourceName, _prompt.SourceName, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<PluginBindingDto?>(_prompt);
            }

            return Task.FromResult<PluginBindingDto?>(null);
        }

        public Task<AgentProviderConfiguration?> GetAgentSecretsAsync(
            Guid tenantId,
            string sourceName,
            CancellationToken ct = default) =>
            Task.FromResult<AgentProviderConfiguration?>(null);
    }
}
