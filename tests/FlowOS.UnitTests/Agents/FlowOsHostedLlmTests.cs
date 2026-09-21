using System.Net;
using System.Text;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Services;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
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

        var resolved = await factory.ResolveAsync(packet, "RiskAnalysisAgent");
        Assert.IsType<TenantLlmWorkflowAgent>(resolved.Agent);
        Assert.Equal("flowos-hosted", resolved.ActorId);
        Assert.Equal("flowos-hosted", resolved.ProviderAlias);
        Assert.Equal("openai", resolved.ProviderName);
        Assert.Equal("gpt-4o-mini", resolved.Model);
        var explicitOverride = await factory.ResolveAsync(packet, "hosted-reviewer-v2");
        Assert.Equal("hosted-reviewer-v2", explicitOverride.ActorId);

        var result = await resolved.Agent.ExecuteAsync(AgentContext.FromPacket(packet));
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
        var resolved = await factory.ResolveAsync(Packet(null));
        Assert.IsType<TenantLlmWorkflowAgent>(resolved.Agent);
        Assert.Equal("flowos-hosted", resolved.ActorId);
    }

    [Fact]
    public async Task Factory_EmptyProvider_UsesRiskWhenHostedMissing()
    {
        var factory = new WorkflowAgentFactory(Mock.Of<IPluginBindingRegistryService>());
        var resolved = await factory.ResolveAsync(Packet(null));
        Assert.IsType<RiskAnalysisAgent>(resolved.Agent);
        Assert.Equal("flowos-risk", resolved.ActorId);
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
            () => factory.ResolveAsync(
                Packet(new AgentProviderRef("flowos-hosted", "flowos-hosted", null, null, false)),
                "RiskAnalysisAgent"));
        Assert.StartsWith(FlowOsHostedLlmCodes.Unavailable, error.Message);
    }

    [Fact]
    public async Task Factory_ByoProvider_DoesNotReserveHostedQuota()
    {
        var tenantId = Guid.NewGuid();
        var bindings = new Mock<IPluginBindingRegistryService>();
        bindings
            .Setup(x => x.GetAgentSecretsAsync(
                tenantId,
                "tenant-openai",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentProviderConfiguration
            {
                ApiKey = "tenant-key",
                Model = "gpt-4o-mini"
            });
        var hosted = new Mock<IFlowOsHostedLlmRuntime>(MockBehavior.Strict);
        var factory = new WorkflowAgentFactory(
            bindings.Object,
            hosted: hosted.Object);
        var packet = Packet(new AgentProviderRef(
            "tenant-openai",
            "openai",
            "gpt-4o-mini",
            null,
            true)) with
        {
            TenantId = tenantId
        };

        var resolved = await factory.ResolveAsync(packet);

        Assert.Equal("tenant-openai", resolved.ActorId);
        Assert.Null(resolved.UsageReservation);
        hosted.Verify(
            x => x.TryLeaseAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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
            ["FlowOS:HostedLlm:MaxCompletionsPerDay"] = "2"
        }).Build();

        var dbOptions = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase($"hosted-llm-{Guid.NewGuid():N}")
            .Options;
        await using var db = new FlowOSDbContext(dbOptions);
        var usageStore = new HostedLlmUsageStore(db);
        var runtime = new FlowOsHostedLlmRuntime(config, entitlement.Object, usageStore);
        Assert.True(runtime.IsConfigured);
        Assert.True(runtime.PublicSettings.HasApiKey);

        var tenantId = Guid.NewGuid();
        var first = await runtime.TryLeaseAsync(tenantId);
        Assert.True(first.Allowed);
        Assert.Equal("sk-platform", first.ApiKey);

        var second = await runtime.TryLeaseAsync(tenantId);
        Assert.True(second.Allowed);
        var third = await runtime.TryLeaseAsync(tenantId);
        Assert.False(third.Allowed);
        Assert.Equal(FlowOsHostedLlmCodes.Quota, third.Code);

        await runtime.FinalizeAsync(first, succeeded: true, inputTokens: 12, outputTokens: 4);
        await runtime.FinalizeAsync(second, succeeded: false, inputTokens: 3, outputTokens: 1);
        var usage = await usageStore.ReadAsync(
            tenantId,
            first.UsageDateUtc!.Value,
            first.Model!);
        Assert.NotNull(usage);
        Assert.Equal(2, usage!.FinalizedRequests);
        Assert.Equal(1, usage.SuccessfulRequests);
        Assert.Equal(1, usage.FailedRequests);
        Assert.Equal(15, usage.InputTokens);
        Assert.Equal(5, usage.OutputTokens);

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
