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

        var resolved = await factory.ResolveAsync(packet, "RiskAnalysisAgent");
        Assert.IsType<TenantLlmWorkflowAgent>(resolved.Agent);
        Assert.Equal("quote-llm", resolved.ActorId);
        Assert.Equal("quote-llm", resolved.ProviderAlias);
        Assert.Equal("openai", resolved.ProviderName);
        var explicitOverride = await factory.ResolveAsync(packet, "quote-reviewer-v2");
        Assert.Equal("quote-reviewer-v2", explicitOverride.ActorId);

        var result = await resolved.Agent.ExecuteAsync(AgentContext.FromPacket(packet));
        Assert.False(result.Success);
        Assert.Equal(AgentFailureCodes.InvalidModelOutput, result.FailureCode);
        Assert.Empty(result.SuggestedActions);
        Assert.DoesNotContain("sk-secret", result.FailureReason ?? "", StringComparison.Ordinal);
        Assert.Contains("outside the legal nextSteps", result.FailureReason ?? "", StringComparison.OrdinalIgnoreCase);
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

        var resolved = await factory.ResolveAsync(packet, "RiskAnalysisAgent");
        Assert.IsType<RiskAnalysisAgent>(resolved.Agent);
        Assert.Equal("flowos-risk", resolved.ActorId);
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

    [Fact]
    public async Task HostedAgent_AnthropicAdapter_BuildsAnthropicHeadersAndExtractsContent()
    {
        var handler = new StubHandler("""
            {"content":[{"type":"text","text":"{\"eventType\":\"QUOTE_APPROVED\",\"confidence\":0.95,\"reason\":\"within limits\",\"insight\":\"approved\"}"}]}
            """);

        var agent = new TenantLlmWorkflowAgent(
            "anthropic",
            "claude-3-5-sonnet-20241022",
            "https://api.anthropic.com/v1/messages",
            "sk-ant-test-key",
            handler);

        var packet = new DecisionPacket(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ApproveQuote",
            "Quoted",
            "HumanTask",
            "Agent",
            null,
            null,
            new Dictionary<string, object?>(),
            new[] { "QUOTE_APPROVED" },
            new[] { "QUOTE_APPROVED" },
            Array.Empty<string>(),
            Array.Empty<SlaReminderFact>(),
            null,
            new Dictionary<string, object>(),
            "Decide",
            null);

        var result = await agent.ExecuteAsync(AgentContext.FromPacket(packet));
        Assert.True(result.Success);
        Assert.Single(result.SuggestedActions);
        Assert.Equal("QUOTE_APPROVED", result.SuggestedActions[0].EventType);
        Assert.Equal(0.95, result.SuggestedActions[0].Confidence);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.Contains("x-api-key"));
        Assert.True(handler.LastRequest.Headers.Contains("anthropic-version"));
    }

    [Fact]
    public async Task HostedAgent_GoogleAdapter_UsesSecretHeaderAndExtractsContent()
    {
        var handler = new StubHandler("""
            {"candidates":[{"content":{"parts":[{"text":"{\"eventType\":\"QUOTE_APPROVED\",\"confidence\":0.91,\"reason\":\"valid quote\",\"insight\":\"approved\"}"}]}}]}
            """);

        var agent = new TenantLlmWorkflowAgent(
            "google",
            "gemini-1.5-flash",
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent",
            "AIza-google-key",
            handler);

        var packet = new DecisionPacket(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ApproveQuote",
            "Quoted",
            "HumanTask",
            "Agent",
            null,
            null,
            new Dictionary<string, object?>(),
            new[] { "QUOTE_APPROVED" },
            new[] { "QUOTE_APPROVED" },
            Array.Empty<string>(),
            Array.Empty<SlaReminderFact>(),
            null,
            new Dictionary<string, object>(),
            "Decide",
            null);

        var result = await agent.ExecuteAsync(AgentContext.FromPacket(packet));
        Assert.True(result.Success);
        Assert.Single(result.SuggestedActions);
        Assert.Equal("QUOTE_APPROVED", result.SuggestedActions[0].EventType);
        Assert.Equal(0.91, result.SuggestedActions[0].Confidence);

        Assert.NotNull(handler.LastUri);
        Assert.DoesNotContain("AIza-google-key", handler.LastUri.Query);
        Assert.True(handler.LastRequest!.Headers.Contains("x-goog-api-key"));
    }

    [Fact]
    public async Task HostedAgent_RequestsStrictSchema_AndReturnsSanitizedTokenTelemetry()
    {
        var handler = new StubHandler(
            """
            {
              "id":"chatcmpl_safe-123",
              "choices":[{"message":{"content":"{\"eventType\":\"QUOTE_APPROVED\",\"confidence\":0.94,\"reason\":\"valid\",\"insight\":\"approved\"}"}}],
              "usage":{"prompt_tokens":21,"completion_tokens":7,"total_tokens":28}
            }
            """,
            requestId: "req_safe-456");
        var agent = new TenantLlmWorkflowAgent(
            "openai",
            "gpt-4o-mini",
            null,
            "test-key",
            handler);

        var result = await agent.ExecuteAsync(AgentContext.FromPacket(Packet("QUOTE_APPROVED")));

        Assert.True(result.Success);
        Assert.NotNull(result.Telemetry);
        Assert.Equal(200, result.Telemetry!.HttpStatusCode);
        Assert.Equal("req_safe-456", result.Telemetry.ProviderRequestId);
        Assert.Equal(21, result.Telemetry.InputTokens);
        Assert.Equal(7, result.Telemetry.OutputTokens);
        Assert.Equal(28, result.Telemetry.TotalTokens);
        Assert.Contains("\"type\":\"json_schema\"", handler.LastBody);
        Assert.Contains("\"strict\":true", handler.LastBody);
        Assert.DoesNotContain("test-key", handler.LastBody);
    }

    [Theory]
    [InlineData("""{"eventType":"QUOTE_APPROVED","reason":"valid","insight":"approved"}""")]
    [InlineData("""{"eventType":"QUOTE_APPROVED","confidence":"0.9","reason":"valid","insight":"approved"}""")]
    [InlineData("""{"eventType":"QUOTE_APPROVED","confidence":1.1,"reason":"valid","insight":"approved"}""")]
    [InlineData("""{"eventType":"","confidence":0.9,"reason":"valid","insight":"approved"}""")]
    [InlineData("""{"eventType":"QUOTE_APPROVED","confidence":0.9,"reason":4,"insight":"approved"}""")]
    public async Task HostedAgent_RejectsMalformedOrOutOfRangeSuggestions(string suggestion)
    {
        var providerBody = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = suggestion } }
            }
        });
        var agent = new TenantLlmWorkflowAgent(
            "openai",
            "gpt-4o-mini",
            null,
            "test-key",
            new StubHandler(providerBody));

        var result = await agent.ExecuteAsync(AgentContext.FromPacket(Packet("QUOTE_APPROVED")));

        Assert.False(result.Success);
        Assert.Equal(AgentFailureCodes.InvalidModelOutput, result.FailureCode);
        Assert.Empty(result.SuggestedActions);
    }

    [Fact]
    public async Task HostedAgent_RejectsOversizedSuggestionStrings()
    {
        var suggestion = System.Text.Json.JsonSerializer.Serialize(new
        {
            eventType = "QUOTE_APPROVED",
            confidence = 0.9,
            reason = new string('x', 2001),
            insight = "approved"
        });
        var providerBody = System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = suggestion } } }
        });
        var agent = new TenantLlmWorkflowAgent(
            "openai",
            "gpt-4o-mini",
            null,
            "test-key",
            new StubHandler(providerBody));

        var result = await agent.ExecuteAsync(AgentContext.FromPacket(Packet("QUOTE_APPROVED")));

        Assert.False(result.Success);
        Assert.Equal(AgentFailureCodes.InvalidModelOutput, result.FailureCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AgentFailureCodes.ProviderAuth)]
    [InlineData(HttpStatusCode.TooManyRequests, AgentFailureCodes.ProviderRateLimit)]
    [InlineData(HttpStatusCode.RequestTimeout, AgentFailureCodes.ProviderTimeout)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AgentFailureCodes.ProviderUnavailable)]
    public async Task HostedAgent_MapsProviderFailuresToStableCodes(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        var agent = new TenantLlmWorkflowAgent(
            "openai",
            "gpt-4o-mini",
            null,
            "test-key",
            new StubHandler("{}", statusCode));

        var result = await agent.ExecuteAsync(AgentContext.FromPacket(Packet("QUOTE_APPROVED")));

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.FailureCode);
        Assert.Equal((int)statusCode, result.Telemetry?.HttpStatusCode);
    }

    [Fact]
    public async Task HostedAgent_PropagatesCancellationToHttpHandler()
    {
        var handler = new BlockingHandler();
        var agent = new TenantLlmWorkflowAgent(
            "openai",
            "gpt-4o-mini",
            null,
            "test-key",
            handler);
        using var cancellation = new CancellationTokenSource();

        var execution = agent.ExecuteAsync(
            AgentContext.FromPacket(Packet("QUOTE_APPROVED")),
            cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.True(handler.CancellationObserved);
    }

    private static DecisionPacket Packet(params string[] legalEvents) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "AgentReview",
            "Waiting",
            "HumanTask",
            "Agent",
            null,
            null,
            new Dictionary<string, object?>(),
            legalEvents,
            legalEvents,
            Array.Empty<string>(),
            Array.Empty<SlaReminderFact>(),
            null,
            new Dictionary<string, object>(),
            "Decide",
            null);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _statusCode;
        private readonly string? _requestId;
        public Uri? LastUri { get; private set; }
        public string LastAuthorization { get; private set; } = string.Empty;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        public StubHandler(
            string body,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string? requestId = null)
        {
            _body = body;
            _statusCode = statusCode;
            _requestId = requestId;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
            LastBody = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            if (_requestId != null)
                response.Headers.TryAddWithoutValidation("x-request-id", _requestId);
            return response;
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }

            throw new InvalidOperationException("Unreachable.");
        }
    }
}
