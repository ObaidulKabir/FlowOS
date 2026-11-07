using System.Net;
using System.Net.Http;
using System.Text;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using FlowOS.Agents.Implementations.Adapters;
using FlowOS.Application.Services;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Domain.Entities;
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

        var factory = new WorkflowAgentFactory(bindings.Object, new FlowOS.Agents.Implementations.Adapters.LlmProviderAdapterRegistry(System.Array.Empty<FlowOS.Agents.Implementations.Adapters.ILlmProviderAdapterFactory>()), handler);
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
        var factory = new WorkflowAgentFactory(Mock.Of<IPluginBindingRegistryService>(), new FlowOS.Agents.Implementations.Adapters.LlmProviderAdapterRegistry(System.Array.Empty<FlowOS.Agents.Implementations.Adapters.ILlmProviderAdapterFactory>()));
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

    /// <summary>
    /// Tenant integration finding N4: an optional top-level "structuredOutput" object alongside
    /// insight/actions carries business data the agent extracted for this step into
    /// AgentResult.StructuredData.
    /// </summary>
    [Fact]
    public async Task HostedAgent_StructuredOutputObject_PopulatesResultStructuredData()
    {
        var handler = new StubHandler("""
            {"content":[{"type":"text","text":"{\"eventType\":\"QUOTE_APPROVED\",\"confidence\":0.95,\"reason\":\"within limits\",\"insight\":\"approved\",\"structuredOutput\":{\"amount\":1500,\"currency\":\"USD\"}}"}]}
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
        Assert.True(result.StructuredData.ContainsKey("amount"));
        Assert.True(result.StructuredData.ContainsKey("currency"));
    }

    /// <summary>
    /// A schema-constrained provider (OpenAI strict mode) must carry structuredOutput as a
    /// JSON-encoded string rather than a nested object; the parser accepts that shape too.
    /// </summary>
    [Fact]
    public async Task HostedAgent_StructuredOutputAsJsonString_IsParsed()
    {
        var handler = new StubHandler("""
            {"content":[{"type":"text","text":"{\"eventType\":\"QUOTE_APPROVED\",\"confidence\":0.95,\"reason\":\"within limits\",\"insight\":\"approved\",\"structuredOutput\":\"{\\\"amount\\\":1500}\"}"}]}
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
        Assert.True(result.StructuredData.ContainsKey("amount"));
    }

    /// <summary>
    /// StructuredOutput defect report: AgentResult.StructuredData's nested values must be plain CLR
    /// types (string/List&lt;object&gt;/Dictionary&lt;string,object&gt;/etc.), never a boxed
    /// System.Text.Json.JsonElement. Deserializing structuredOutput straight into
    /// Dictionary&lt;string, object&gt; boxes every nested value as a JsonElement, which the
    /// Newtonsoft.Json serializer on the MCP response path cannot read by value - it reflects over
    /// JsonElement's only public property instead, producing {"ValueKind":2}/{"ValueKind":3} stubs in
    /// place of the real array/string data. This reproduces the report's exact shape: a nested array
    /// ("questions") and a nested string ("notes"), carried as a JSON-encoded structuredOutput string
    /// the way a schema-constrained provider emits it.
    /// </summary>
    [Fact]
    public async Task HostedAgent_StructuredOutputWithNestedArrayAndString_YieldsPlainClrValues()
    {
        var handler = new StubHandler("""
            {"content":[{"type":"text","text":"{\"eventType\":\"QUOTE_APPROVED\",\"confidence\":0.95,\"reason\":\"within limits\",\"insight\":\"approved\",\"structuredOutput\":\"{\\\"questions\\\":[{\\\"stem\\\":\\\"What was the fuel burn?\\\"}],\\\"notes\\\":\\\"all good\\\"}\"}"}]}
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

        // The array value must be a real List<object> (or similar enumerable of plain values), never a
        // boxed JsonElement whose only public property is ValueKind.
        var questions = Assert.IsType<List<object>>(result.StructuredData["questions"]);
        Assert.Single(questions);
        var firstQuestion = Assert.IsType<Dictionary<string, object>>(questions[0]);
        Assert.Equal("What was the fuel burn?", firstQuestion["stem"]);

        // The string value must be a real string, never a boxed JsonElement.
        var notes = Assert.IsType<string>(result.StructuredData["notes"]);
        Assert.Equal("all good", notes);

        // Nothing in StructuredData should be a System.Text.Json.JsonElement - that is precisely the
        // defect this test guards against.
        Assert.All(result.StructuredData.Values, value =>
            Assert.False(value is System.Text.Json.JsonElement, "StructuredData must not contain boxed JsonElement values."));
    }

    /// <summary>
    /// Absent structuredOutput must not change existing behavior: StructuredData stays the
    /// AgentResult default (empty), exactly as before this field existed.
    /// </summary>
    [Fact]
    public async Task HostedAgent_NoStructuredOutput_LeavesStructuredDataEmpty()
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
        Assert.Empty(result.StructuredData);
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
        // DX report F12: FailureField/Limit/ActualLength are only populated for FIELD_OVERFLOW.
        Assert.Null(result.FailureField);
        Assert.Null(result.FailureLimit);
        Assert.Null(result.FailureActualLength);
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

    /// <summary>
    /// DecisionPacket report, R1: before this, an over-length insight fell through to the same
    /// generic "did not match the required suggestion contract" message as any other malformed
    /// reply, so a caller couldn't tell "insight was too long" apart from "the model emitted
    /// garbage" without re-parsing the raw provider payload themselves. FailureReason must now name
    /// the actual length and point at structuredOutput as the uncapped alternative.
    /// </summary>
    [Fact]
    public async Task HostedAgent_OverLengthInsight_FailsWithASpecificDiagnosableReason()
    {
        var overLengthInsight = new string('x', 2001);
        var suggestion = System.Text.Json.JsonSerializer.Serialize(new
        {
            eventType = "QUOTE_APPROVED",
            confidence = 0.9,
            reason = "valid",
            insight = overLengthInsight
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
        // DX report F12: this now reports the typed FIELD_OVERFLOW code with structured context
        // (field/limit/actualLength), not the generic INVALID_MODEL_OUTPUT every other malformed
        // reply still gets. FailureReason keeps the same human-readable message.
        Assert.Equal(AgentFailureCodes.FieldOverflow, result.FailureCode);
        Assert.Equal("insight", result.FailureField);
        Assert.Equal(2000, result.FailureLimit);
        Assert.Equal(2001, result.FailureActualLength);
        Assert.Contains("2001 characters", result.FailureReason ?? "", StringComparison.Ordinal);
        Assert.Contains("2000-character limit", result.FailureReason ?? "", StringComparison.Ordinal);
        Assert.Contains("structuredOutput", result.FailureReason ?? "", StringComparison.Ordinal);
    }

    /// <summary>At exactly the cap, insight is accepted unchanged - the cap is inclusive.</summary>
    [Fact]
    public async Task HostedAgent_InsightAtExactCap_Succeeds()
    {
        var exactCapInsight = new string('x', 2000);
        var suggestion = System.Text.Json.JsonSerializer.Serialize(new
        {
            eventType = "QUOTE_APPROVED",
            confidence = 0.9,
            reason = "valid",
            insight = exactCapInsight
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

        Assert.True(result.Success);
        Assert.Equal(exactCapInsight, result.Insight);
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

    [Fact]
    public async Task TenantDefaultProvider_ResolvedByWorkflowAgentFactory_WhenDefaultRequested()
    {
        var tenantId = Guid.NewGuid();
        var handler = new StubHandler("""
            {"choices":[{"message":{"content":"{\"eventType\":\"QUOTE_APPROVED\",\"confidence\":0.95,\"reason\":\"valid\",\"insight\":\"approved\"}"}}]}
            """);

        var bindings = new Mock<IPluginBindingRegistryService>();
        bindings.Setup(x => x.GetAgentSecretsAsync(tenantId, "default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentProviderConfiguration
            {
                Model = "gpt-4o",
                Endpoint = "https://api.openai.com/v1/chat/completions",
                ApiKey = "sk-default-key",
                IsDefault = true
            });

        var factory = new WorkflowAgentFactory(bindings.Object, new FlowOS.Agents.Implementations.Adapters.LlmProviderAdapterRegistry(System.Array.Empty<FlowOS.Agents.Implementations.Adapters.ILlmProviderAdapterFactory>()), handler);
        var packet = new DecisionPacket(
            tenantId,
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
            "Decide the quote",
            null,
            new AgentProviderRef("default", "openai", "gpt-4o", null, true));

        var resolved = await factory.ResolveAsync(packet);
        Assert.NotNull(resolved);
        Assert.Equal("default", resolved.ProviderAlias);
        Assert.Equal("openai", resolved.ProviderName);
        Assert.Equal("gpt-4o", resolved.Model);

        var result = await resolved.Agent.ExecuteAsync(AgentContext.FromPacket(packet));
        Assert.True(result.Success);
        Assert.Single(result.SuggestedActions);
        Assert.Equal("QUOTE_APPROVED", result.SuggestedActions[0].EventType);
    }

    [Fact]
    public async Task HostedAgent_AnthropicAdapter_MapsOverloadedErrorToProviderUnavailable()
    {
        var handler = new StubHandler("""
            {"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}
            """, HttpStatusCode.OK);

        var agent = new TenantLlmWorkflowAgent(
            "anthropic",
            "claude-3-5-sonnet-20241022",
            "https://api.anthropic.com/v1/messages",
            "sk-ant-test-key",
            handler);

        var result = await agent.ExecuteAsync(AgentContext.FromPacket(Packet("QUOTE_APPROVED")));

        Assert.False(result.Success);
        Assert.Equal(AgentFailureCodes.ProviderUnavailable, result.FailureCode);
    }

    [Fact]
    public async Task HostedAgent_GoogleAdapter_MapsSafetyFinishReasonToInvalidModelOutput()
    {
        var handler = new StubHandler("""
            {"candidates":[{"finishReason":"SAFETY","content":{"parts":[]}}]}
            """);

        var agent = new TenantLlmWorkflowAgent(
            "google",
            "gemini-1.5-flash",
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent",
            "AIza-google-key",
            handler);

        var result = await agent.ExecuteAsync(AgentContext.FromPacket(Packet("QUOTE_APPROVED")));

        Assert.False(result.Success);
        Assert.Equal(AgentFailureCodes.InvalidModelOutput, result.FailureCode);
    }

    [Theory]
    [InlineData("```json\n{\"eventType\":\"QUOTE_APPROVED\",\"confidence\":0.9,\"reason\":\"r\",\"insight\":\"i\"}\n```")]
    [InlineData("```\r\n{\"eventType\":\"QUOTE_APPROVED\",\"confidence\":0.9,\"reason\":\"r\",\"insight\":\"i\"}\r\n```")]
    [InlineData("   ```json   \n{\"eventType\":\"QUOTE_APPROVED\",\"confidence\":0.9,\"reason\":\"r\",\"insight\":\"i\"}\n```   ")]
    public async Task HostedAgent_StripsMarkdownCodeFences(string markdownResponse)
    {
        var safeResponse = markdownResponse.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        var handler = new StubHandler($$"""
            {"content":[{"type":"text","text":"{{safeResponse}}"}]}
            """);

        var agent = new TenantLlmWorkflowAgent(
            "anthropic",
            "claude-3-5-sonnet-20241022",
            "https://api.anthropic.com/v1/messages",
            "sk-ant-test-key",
            handler);

        var result = await agent.ExecuteAsync(AgentContext.FromPacket(Packet("QUOTE_APPROVED")));

        Assert.True(result.Success);
        Assert.Single(result.SuggestedActions);
        Assert.Equal("QUOTE_APPROVED", result.SuggestedActions[0].EventType);
    }

    /// <summary>
    /// Tenant integration finding N5: the prompt auditor's rawRequestPayload must record the
    /// sampling params actually sent on the request, not just the prompt text, so a tenant can
    /// confirm what was effective without re-deriving it from the saved provider configuration.
    /// </summary>
    [Fact]
    public async Task PromptAudit_RecordsEffectiveSamplingParams()
    {
        var handler = new StubHandler("""
            {"choices":[{"message":{"content":"{\"eventType\":\"QUOTE_APPROVED\",\"confidence\":0.95,\"reason\":\"ok\",\"insight\":\"approved\"}"}}]}
            """);

        AgentPromptAuditRecord? captured = null;
        var auditor = new Mock<IAgentPromptAuditor>();
        auditor.Setup(a => a.IsEnabled).Returns(true);
        auditor.Setup(a => a.TryRecordAsync(It.IsAny<AgentPromptAuditRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AgentPromptAuditRecord, CancellationToken>((record, _) => captured = record)
            .Returns(Task.CompletedTask);

        var agent = new TenantLlmWorkflowAgent(
            "openai",
            "gpt-4o-mini",
            "https://llm.example/v1/chat/completions",
            "sk-test-key",
            handler,
            adapter: new OpenAiProviderAdapter(temperature: 0.4, seed: 7),
            auditor: auditor.Object);

        var packet = Packet("QUOTE_APPROVED");
        var result = await agent.ExecuteAsync(AgentContext.FromPacket(packet));

        Assert.True(result.Success);
        Assert.NotNull(captured);
        using var doc = System.Text.Json.JsonDocument.Parse(captured!.RawRequestPayload);
        var sampling = doc.RootElement.GetProperty("EffectiveSamplingParams");
        Assert.Equal(0.4, sampling.GetProperty("Temperature").GetDouble());
        Assert.Equal(7, sampling.GetProperty("Seed").GetInt32());
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
