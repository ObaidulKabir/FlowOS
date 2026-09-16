using System.Net;
using System.Net.Http;
using System.Text;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using FlowOS.Application.Services;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using Moq;

namespace FlowOS.UnitTests.Agents;

public class TenantLlmWorkflowAgentTests
{
    [Fact]
    public async Task HostedAgent_UsesBindingSecret_AndDropsIllegalEvents()
    {
        var tenantId = Guid.NewGuid();
        var handler = new StubHandler("""
            {"choices":[{"message":{"content":"{\"eventType\":\"EVT-HACK\",\"confidence\":0.99,\"reason\":\"invented\",\"insight\":\"bad\"}"}}]}
            """);

        var bindings = new Mock<IPluginBindingRegistryService>();
        bindings.Setup(x => x.GetAgentSecretsAsync(tenantId, "quote-llm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentProviderConfiguration
            {
                Model = "gpt-4o-mini",
                Endpoint = "https://llm.example/v1/chat/completions",
                ApiKey = "sk-secret-must-stay-internal"
            });

        var factory = new WorkflowAgentFactory(bindings.Object, handler);
        var packet = new DecisionPacket(
            tenantId,
            Guid.NewGuid(),
            "ApproveQuote",
            "Quoted",
            "HumanTask",
            "Agent",
            "Guideline",
            "Policy",
            new Dictionary<string, object?>(),
            new[] { "QUOTE_APPROVED" },
            new[] { "QUOTE_APPROVED" },
            Array.Empty<string>(),
            Array.Empty<SlaReminderFact>(),
            "QUOTE_RESPONSE_OVERDUE",
            new Dictionary<string, object>(),
            "Decide the quote",
            new AutoCommitPolicy(0.8, new[] { "QUOTE_APPROVED" }),
            new AgentProviderRef("quote-llm", "openai", "gpt-4o-mini", "https://llm.example/v1", true));

        var agent = await factory.CreateAsync(packet, "RiskAnalysisAgent");
        Assert.IsType<TenantLlmWorkflowAgent>(agent);

        var result = await agent.ExecuteAsync(AgentContext.FromPacket(packet));
        Assert.True(result.Success);
        Assert.Empty(result.SuggestedActions);
        Assert.DoesNotContain("sk-secret", result.Insight ?? "", StringComparison.Ordinal);
        Assert.Contains("outside the legal nextSteps", result.Insight ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://llm.example/v1/chat/completions", handler.LastUri?.ToString());
        Assert.Contains("Bearer sk-secret-must-stay-internal", handler.LastAuthorization);
        var composed = System.Text.Json.JsonSerializer.Serialize(
            FlowOS.Application.Services.AgentContextComposer.FromPacket(packet, "live"));
        Assert.DoesNotContain("sk-secret-must-stay-internal", composed);
        Assert.DoesNotContain("\"apiKey\"", composed);
    }

    [Fact]
    public async Task FlowOsRiskProvider_UsesRiskAnalysisAgent()
    {
        var factory = new WorkflowAgentFactory(Mock.Of<IPluginBindingRegistryService>());
        var packet = new DecisionPacket(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Review",
            "Review",
            "HumanTask",
            "Agent",
            null,
            null,
            new Dictionary<string, object?> { ["Amount"] = 10, ["Category"] = "Office Supplies" },
            new[] { "EVT-APPROVE" },
            new[] { "EVT-APPROVE" },
            Array.Empty<string>(),
            Array.Empty<SlaReminderFact>(),
            null,
            new Dictionary<string, object> { ["Amount"] = 10, ["Category"] = "Office Supplies" },
            "Review expense",
            null,
            new AgentProviderRef("builtin", "flowos-risk", null, null, false));

        var agent = await factory.CreateAsync(packet, "RiskAnalysisAgent");
        Assert.IsType<RiskAnalysisAgent>(agent);
    }

    [Fact]
    public async Task HostedAgent_FailsClosed_WithoutApiKey()
    {
        var agent = new TenantLlmWorkflowAgent("openai", "gpt-4o-mini", null, apiKey: null);
        var result = await agent.ExecuteAsync(new AgentContext(
            Guid.NewGuid(),
            new Dictionary<string, object>(),
            "Start",
            Array.Empty<FlowOS.Events.Abstractions.IEvent>(),
            "Decide"));

        Assert.False(result.Success);
        Assert.Contains("API key", result.FailureReason);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public Uri? LastUri { get; private set; }
        public string LastAuthorization { get; private set; } = string.Empty;

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
