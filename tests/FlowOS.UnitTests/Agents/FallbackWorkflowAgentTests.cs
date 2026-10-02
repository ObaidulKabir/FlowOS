using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using Xunit;

namespace FlowOS.UnitTests.Agents;

public class FixedWorkflowAgent : IWorkflowAgent
{
    protected readonly AgentResult _presetResult;

    public FixedWorkflowAgent(AgentResult presetResult)
    {
        _presetResult = presetResult;
    }

    public virtual Task<AgentResult> ExecuteAsync(AgentContext context)
    {
        return Task.FromResult(_presetResult);
    }
}

public class ConversationalFixedAgent : FixedWorkflowAgent, IConversationalAgent
{
    public ConversationalFixedAgent(AgentResult presetResult) : base(presetResult)
    {
    }

    public int MaxTurns => 1;

    public Task<AgentResult> ExecuteConversationalAsync(
        DecisionPacket packet,
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_presetResult);
    }
}

public class FallbackWorkflowAgentTests
{
    [Fact]
    public async Task PrimarySucceeds_DoesNotCallFallbacks()
    {
        var primary = new FixedWorkflowAgent(AgentResult.FromInsight("ok"));
        var fallbacks = new IWorkflowAgent[]
        {
            new ThrowingWorkflowAgent(),
            new ThrowingWorkflowAgent()
        };

        var agent = new FallbackWorkflowAgent(primary, fallbacks);
        var result = await agent.ExecuteAsync(MakeDummyContext());

        Assert.True(result.Success);
        Assert.Equal("ok", result.Insight);
    }

    [Fact]
    public async Task PrimaryFails_RateLimit_FallbackSucceeds()
    {
        var primary = new FixedWorkflowAgent(AgentResult.Failure(AgentFailureCodes.ProviderRateLimit, "throttled"));
        var fallbacks = new IWorkflowAgent[]
        {
            new FixedWorkflowAgent(AgentResult.FromInsight("fixed by fallback"))
        };

        var agent = new FallbackWorkflowAgent(primary, fallbacks);
        var result = await agent.ExecuteAsync(MakeDummyContext());

        Assert.True(result.Success);
        Assert.Equal("fixed by fallback", result.Insight);
    }

    [Fact]
    public async Task PrimaryFails_Unavailable_FallbackSucceeds()
    {
        var primary = new FixedWorkflowAgent(AgentResult.Failure(AgentFailureCodes.ProviderUnavailable, "503"));
        var fallbacks = new IWorkflowAgent[]
        {
            new FixedWorkflowAgent(AgentResult.FromInsight("fallback success"))
        };

        var agent = new FallbackWorkflowAgent(primary, fallbacks);
        var result = await agent.ExecuteAsync(MakeDummyContext());

        Assert.True(result.Success);
        Assert.Equal("fallback success", result.Insight);
    }

    [Fact]
    public async Task PrimaryFails_Timeout_FallbackSucceeds()
    {
        var primary = new FixedWorkflowAgent(AgentResult.Failure(AgentFailureCodes.ProviderTimeout, "timed out"));
        var fallbacks = new IWorkflowAgent[]
        {
            new FixedWorkflowAgent(AgentResult.FromInsight("fallback success"))
        };

        var agent = new FallbackWorkflowAgent(primary, fallbacks);
        var result = await agent.ExecuteAsync(MakeDummyContext());

        Assert.True(result.Success);
        Assert.Equal("fallback success", result.Insight);
    }

    [Fact]
    public async Task PrimaryFails_NonRetryable_DoesNotFallback()
    {
        var primary = new FixedWorkflowAgent(AgentResult.Failure(AgentFailureCodes.ProviderAuth, "unauthorized"));
        var fallbacks = new IWorkflowAgent[]
        {
            new ThrowingWorkflowAgent()
        };

        var agent = new FallbackWorkflowAgent(primary, fallbacks);
        var result = await agent.ExecuteAsync(MakeDummyContext());

        Assert.False(result.Success);
        Assert.Equal(AgentFailureCodes.ProviderAuth, result.FailureCode);
        Assert.Equal("unauthorized", result.FailureReason);
    }

    [Fact]
    public async Task AllAgentsFailable_ReturnsLastFallbackFailure()
    {
        var primary = new FixedWorkflowAgent(AgentResult.Failure(AgentFailureCodes.ProviderRateLimit, "rate limited"));
        var fallbacks = new IWorkflowAgent[]
        {
            new FixedWorkflowAgent(AgentResult.Failure(AgentFailureCodes.ProviderUnavailable, "unavailable")),
            new FixedWorkflowAgent(AgentResult.Failure(AgentFailureCodes.ProviderTimeout, "timed out"))
        };

        var agent = new FallbackWorkflowAgent(primary, fallbacks);
        var result = await agent.ExecuteAsync(MakeDummyContext());

        Assert.False(result.Success);
        Assert.Equal(AgentFailureCodes.ProviderTimeout, result.FailureCode);
        Assert.Equal("timed out", result.FailureReason);
    }

    [Fact]
    public async Task Conversational_PrimaryRateLimit_TriesFallback()
    {
        var primary = new ConversationalFixedAgent(AgentResult.Failure(AgentFailureCodes.ProviderRateLimit, "throttled"));
        var fallbacks = new IWorkflowAgent[]
        {
            new ConversationalFixedAgent(AgentResult.FromInsight("chat worked"))
        };

        var agent = new FallbackWorkflowAgent(primary, fallbacks);
        var packet = MakeDummyPacket();
        var history = new List<ChatMessage>();
        var result = await agent.ExecuteConversationalAsync(packet, history);

        Assert.True(result.Success);
        Assert.Equal("chat worked", result.Insight);
    }

    [Fact]
    public async Task ExecuteAsync_WithConversationalPrimaries_StillWorks()
    {
        var primary = new ConversationalFixedAgent(AgentResult.FromInsight("conv success"));
        var fallbacks = Array.Empty<IWorkflowAgent>();

        var agent = new FallbackWorkflowAgent(primary, fallbacks);
        var result = await agent.ExecuteAsync(MakeDummyContext());

        Assert.True(result.Success);
        Assert.Equal("conv success", result.Insight);
    }

    private static AgentContext MakeDummyContext()
    {
        return new AgentContext(
            Guid.NewGuid(),
            new Dictionary<string, object>(),
            "Start",
            Array.Empty<FlowOS.Events.Abstractions.IEvent>(),
            "Decide");
    }

    private static DecisionPacket MakeDummyPacket()
    {
        return new DecisionPacket(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "AgentReview",
            "Waiting",
            "HumanTask",
            "Agent",
            null,
            null,
            new Dictionary<string, object?>(),
            new[] { "EVT-APPROVE" },
            new[] { "EVT-APPROVE" },
            Array.Empty<string>(),
            Array.Empty<SlaReminderFact>(),
            null,
            new Dictionary<string, object>(),
            "Decide",
            null);
    }

    private sealed class ThrowingWorkflowAgent : IWorkflowAgent
    {
        public Task<AgentResult> ExecuteAsync(AgentContext context)
        {
            throw new InvalidOperationException("Fallback should not have been called.");
        }
    }
}
