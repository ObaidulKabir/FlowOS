using System.Net;
using System.Text;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Services;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Moq;

namespace FlowOS.UnitTests.Agents;

public class FlowOsHostedLlmTests
{
    [Fact]
    public async Task Factory_UsesPlatformOpenAiKey_ForFlowOsHosted()
    {
        var handler = new StubHandler("""
            {"choices":[{"message":{"content":"{\"eventType\":\"QUOTE_APPROVED\",\"confidence\":0.96,\"reason\":\"in band\",\"insight\":\"ok\"}"}}]}
            """);
        var bindings = new Mock<IPluginBindingRegistryService>(MockBehavior.Strict);
        var hosted = new StubHostedRuntime(
            configured: true,
            lease: new FlowOsHostedLlmLease(true, "sk-platform-openai", "gpt-4o-mini", null, null, null));

        var factory = new WorkflowAgentFactory(bindings.Object, handler, hosted);
        var packet = Packet(new AgentProviderRef("flowos-hosted", "flowos-hosted", "gpt-4o-mini", null, true));

        var agent = await factory.CreateAsync(packet, "RiskAnalysisAgent");
        Assert.IsType<TenantLlmWorkflowAgent>(agent);

        var result = await agent.ExecuteAsync(AgentContext.FromPacket(packet));
        Assert.True(result.Success);
        Assert.Equal("QUOTE_APPROVED", result.SuggestedActions[0].EventType);
        Assert.Contains("Bearer sk-platform-openai", handler.LastAuthorization);
        bindings.Verify(
            x => x.GetAgentSecretsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Factory_EmptyProvider_UsesHostedWhenConfigured()
    {
        var hosted = new StubHostedRuntime(
            configured: true,
            lease: new FlowOsHostedLlmLease(true, "sk-platform-openai", "gpt-4o-mini", null, null, null));
        var factory = new WorkflowAgentFactory(Mock.Of<IPluginBindingRegistryService>(), hosted: hosted);
        var agent = await factory.CreateAsync(Packet(null), "RiskAnalysisAgent");
        Assert.IsType<TenantLlmWorkflowAgent>(agent);
    }

    [Fact]
    public async Task Factory_EmptyProvider_UsesRiskWhenHostedMissing()
    {
        var factory = new WorkflowAgentFactory(Mock.Of<IPluginBindingRegistryService>());
        var agent = await factory.CreateAsync(Packet(null), "RiskAnalysisAgent");
        Assert.IsType<RiskAnalysisAgent>(agent);
    }

    [Fact]
    public async Task Factory_ThrowsUnavailable_WhenHostedLeaseDenied()
    {
        var hosted = new StubHostedRuntime(
            configured: false,
            lease: new FlowOsHostedLlmLease(
                false, null, "gpt-4o-mini", null,
                FlowOsHostedLlmCodes.Unavailable,
                FlowOsHostedLlmCodes.UnavailableMessage));
        var factory = new WorkflowAgentFactory(Mock.Of<IPluginBindingRegistryService>(), hosted: hosted);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateAsync(
                Packet(new AgentProviderRef("flowos-hosted", "flowos-hosted", null, null, false)),
                "RiskAnalysisAgent"));
        Assert.StartsWith(FlowOsHostedLlmCodes.Unavailable, error.Message);
    }

    [Fact]
    public async Task Runtime_DeniesTrialAndCapsDailyQuota()
    {
        var entitlement = new Mock<ITenantEntitlementService>();
        entitlement.Setup(x => x.EnsureRuntimeAllowedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantEntitlementDecision(true));

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FLOWOS_HOSTED_LLM_API_KEY"] = "sk-platform",
            ["FlowOS:HostedLlm:MaxCompletionsPerDay"] = "1"
        }).Build();

        var runtime = new FlowOsHostedLlmRuntime(config, entitlement.Object);
        Assert.True(runtime.IsConfigured);
        Assert.True(runtime.PublicSettings.HasApiKey);

        var tenantId = Guid.NewGuid();
        var first = await runtime.TryLeaseAsync(tenantId);
        Assert.True(first.Allowed);
        Assert.Equal("sk-platform", first.ApiKey);

        var second = await runtime.TryLeaseAsync(tenantId);
        Assert.False(second.Allowed);
        Assert.Equal(FlowOsHostedLlmCodes.Quota, second.Code);

        entitlement.Setup(x => x.EnsureRuntimeAllowedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantEntitlementDecision(
                false, TenantEntitlementPolicy.PlanRequiredCode, TenantEntitlementPolicy.PlanRequiredMessage));
        var unpaid = await runtime.TryLeaseAsync(Guid.NewGuid());
        Assert.False(unpaid.Allowed);
        Assert.Equal(TenantEntitlementPolicy.PlanRequiredCode, unpaid.Code);
    }

    private static DecisionPacket Packet(AgentProviderRef? provider) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "AgentReview",
            "AgentQueued",
            "HumanTask",
            "Agent",
            "Guideline",
            "Policy",
            new Dictionary<string, object?>(),
            new[] { "QUOTE_APPROVED" },
            new[] { "QUOTE_APPROVED" },
            Array.Empty<string>(),
            Array.Empty<SlaReminderFact>(),
            null,
            new Dictionary<string, object>(),
            "Decide",
            new AutoCommitPolicy(0.8, new[] { "QUOTE_APPROVED" }),
            provider);

    private sealed class StubHostedRuntime : IFlowOsHostedLlmRuntime
    {
        private readonly FlowOsHostedLlmLease _lease;

        public StubHostedRuntime(bool configured, FlowOsHostedLlmLease lease)
        {
            IsConfigured = configured;
            _lease = lease;
            PublicSettings = new FlowOsHostedLlmPublicSettings(
                configured, configured, "openai", "gpt-4o-mini", null, 200);
        }

        public bool IsConfigured { get; }
        public FlowOsHostedLlmPublicSettings PublicSettings { get; }

        public Task<FlowOsHostedLlmLease> TryLeaseAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_lease);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public string LastAuthorization { get; private set; } = string.Empty;

        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
