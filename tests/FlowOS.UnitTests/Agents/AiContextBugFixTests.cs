using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Agents.Abstractions;
using FlowOS.Agents.Implementations;
using FlowOS.Agents.Implementations.Adapters;
using FlowOS.Application.Services;

namespace FlowOS.UnitTests.Agents;

// ─────────────────────────────────────────────────────────────────────────────
// Bug fix 1: ExecuteConversationalAsync must forward the IAgentToolInvoker so
// the model can call tools when using the conversational path.
// ─────────────────────────────────────────────────────────────────────────────
public class ConversationalPathToolInvokerTests
{
    [Fact]
    public async Task ExecuteConversationalAsync_ForwardsToolInvoker_SoModelCanCallTools()
    {
        // Two-turn: first response is a tool call, second is the final suggestion.
        var callCount = 0;
        var handler = new SequencedStubHandler(
            new[]
            {
                """{"choices":[{"message":{"tool_calls":[{"type":"function","id":"call-1","function":{"name":"lookup","arguments":"{}"}}]}}]}""",
                """{"choices":[{"message":{"content":"{\"insight\":\"done\",\"actions\":[{\"eventType\":\"APPROVE\",\"confidence\":0.9,\"reason\":\"ok\"}]}"}}]}"""
            },
            onRequest: _ => { callCount++; return Task.CompletedTask; });

        var invokerCalled = false;
        var invoker = new CapturingToolInvoker(() => { invokerCalled = true; });

        var agent = MakeAgent(handler);
        var packet = MakePacket(new[] { "APPROVE" });

        var result = await agent.ExecuteConversationalAsync(
            packet,
            history: Array.Empty<ChatMessage>(),
            toolInvoker: invoker,
            cancellationToken: CancellationToken.None);

        Assert.True(result.Success, result.FailureReason);
        Assert.True(invokerCalled, "ToolInvoker must be called when the model requests a tool.");
        Assert.Equal(2, callCount); // tool-call turn + final turn
    }

    [Fact]
    public async Task ExecuteConversationalAsync_NoToolInvoker_FailsWithClearCode_WhenModelCallsTool()
    {
        // Model returns a tool call but no invoker is supplied — must fail gracefully.
        var handler = new SequencedStubHandler(
            """{"choices":[{"message":{"tool_calls":[{"type":"function","id":"call-1","function":{"name":"lookup","arguments":"{}"}}]}}]}""");

        var agent = MakeAgent(handler);
        var packet = MakePacket(new[] { "APPROVE" });

        var result = await agent.ExecuteConversationalAsync(
            packet,
            history: Array.Empty<ChatMessage>(),
            toolInvoker: null,
            cancellationToken: CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(AgentFailureCodes.ProviderConfiguration, result.FailureCode);
    }

    private static TenantLlmWorkflowAgent MakeAgent(SequencedStubHandler handler) =>
        new("openai", "gpt-4o-mini", "", "sk-test", handler);

    private static DecisionPacket MakePacket(string[] legalEvents) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ReviewStep",
            "Pending",
            "HumanTask",
            "Agent",
            "Guideline",
            "Policy",
            new Dictionary<string, object?>(),
            legalEvents,
            legalEvents,
            Array.Empty<string>(),
            Array.Empty<SlaReminderFact>(),
            null,
            new Dictionary<string, object>(),
            "Decide",
            new AutoCommitPolicy(0.8, legalEvents),
            null);

    private sealed class CapturingToolInvoker : IAgentToolInvoker
    {
        private readonly Action _onInvoke;
        public CapturingToolInvoker(Action onInvoke) { _onInvoke = onInvoke; }

        public Task<string> InvokeToolAsync(string toolName, string? argumentsJson, CancellationToken cancellationToken = default)
        {
            _onInvoke();
            return Task.FromResult("{\"result\":\"ok\"}");
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Bug fix 2: All adapters must include the user prompt (workflow packet) even
// when history messages are present.
// ─────────────────────────────────────────────────────────────────────────────
public class AdapterPacketPersistenceTests
{
    [Fact]
    public async Task AnthropicAdapter_WithHistory_StillIncludesUserPrompt()
    {
        var adapter = new AnthropicProviderAdapter();
        var history = new List<ToolCallMessage>
        {
            new("assistant", "Previous insight", null)
        };

        var request = adapter.CreateRequest(
            "", "sk-ant", "claude-3-5-sonnet-20241022",
            "system prompt", "workflow packet payload",
            null, history);

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var messages = doc.RootElement.GetProperty("messages");

        // The workflow packet must appear as the first user message even with history.
        Assert.True(messages.GetArrayLength() >= 2,
            "Must have at least the packet user message and the history assistant message.");

        var firstMsg = messages[0];
        Assert.Equal("user", firstMsg.GetProperty("role").GetString());

        var firstContent = firstMsg.GetProperty("content");
        // Content may be a string (simple case) or an array of blocks.
        if (firstContent.ValueKind == JsonValueKind.String)
        {
            Assert.Equal("workflow packet payload", firstContent.GetString());
        }
        else
        {
            var text = firstContent[0].GetProperty("text").GetString();
            Assert.Equal("workflow packet payload", text);
        }
    }

    [Fact]
    public async Task AnthropicAdapter_WithNoHistory_IncludesUserPrompt()
    {
        var adapter = new AnthropicProviderAdapter();
        var request = adapter.CreateRequest(
            "", "sk-ant", "claude-3-5-sonnet-20241022",
            "system", "packet", null, null);

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task OpenAiAdapter_AlwaysIncludesUserPrompt_AcrossIterations()
    {
        // Simulate second tool-loop iteration where userPrompt is the full packet.
        var adapter = new OpenAiProviderAdapter();
        var history = new List<ToolCallMessage>
        {
            new("assistant", null, new List<ToolCallInfo>
            {
                new("call-1", "lookup", "{}")
            }),
            new("tool", "{\"ok\":true}", ToolCallId: "call-1", ToolName: "lookup")
        };

        var request = adapter.CreateRequest(
            "", "sk-test", "gpt-4o-mini",
            "system", "packet payload",
            null, history);

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var messages = doc.RootElement.GetProperty("messages");

        // messages[0]=system, messages[1]=user with packet, then history
        Assert.True(messages.GetArrayLength() >= 2);
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("packet payload", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task TenantLlmWorkflowAgent_SendsPacketOnToolLoopIterations()
    {
        // Capture request bodies and verify the user prompt appears on every request.
        var bodies = new List<string>();
        string[] responses =
        [
            """{"choices":[{"message":{"tool_calls":[{"type":"function","id":"c1","function":{"name":"lookup","arguments":"{}"}}]}}]}""",
            """{"choices":[{"message":{"content":"{\"insight\":\"ok\",\"actions\":[{\"eventType\":\"APPROVE\",\"confidence\":0.9,\"reason\":\"r\"}]}"}}]}"""
        ];
        var handler = new SequencedStubHandler(
            responses,
            onRequest: async req =>
            {
                var b = await req.Content!.ReadAsStringAsync();
                bodies.Add(b);
            });

        var invoker = new EchoToolInvoker();
        var agent = new TenantLlmWorkflowAgent("openai", "gpt-4o-mini", "", "sk-test", handler);
        var packet = MakeMinimalPacket();

        await agent.ExecuteConversationalAsync(packet, Array.Empty<ChatMessage>(), invoker);

        Assert.Equal(2, bodies.Count);
        foreach (var body in bodies)
        {
            using var doc = JsonDocument.Parse(body);
            var messages = doc.RootElement.GetProperty("messages");
            // All requests must contain the packet as a user message (index 1).
            bool hasUserMessage = false;
            foreach (var msg in messages.EnumerateArray())
            {
                if (msg.TryGetProperty("role", out var role) &&
                    role.GetString() == "user" &&
                    msg.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(content.GetString()))
                {
                    hasUserMessage = true;
                    break;
                }
            }
            Assert.True(hasUserMessage, "Every request must include the workflow packet as a non-empty user message.");
        }
    }

    private static DecisionPacket MakeMinimalPacket() =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), "S", "State", "HumanTask", "Agent",
            "G", "P", new Dictionary<string, object?>(), new[] { "APPROVE" },
            new[] { "APPROVE" }, Array.Empty<string>(), Array.Empty<SlaReminderFact>(),
            null, new Dictionary<string, object>(), "Decide",
            new AutoCommitPolicy(0.8, new[] { "APPROVE" }), null);

    private sealed class EchoToolInvoker : IAgentToolInvoker
    {
        public Task<string> InvokeToolAsync(string toolName, string? argumentsJson, CancellationToken cancellationToken = default)
            => Task.FromResult("{\"ok\":true}");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Bug fix 3: Legal next-step events must NOT appear as callable tools.
// The tool list should contain only declared plugin/resource/connector tools.
// ─────────────────────────────────────────────────────────────────────────────
public class AgentToolCatalogEventsNotAsToolsTests
{
    [Fact]
    public void FromStep_DoesNotAddLegalEventsAsTools()
    {
        var legalEvents = new[] { "APPROVE", "REJECT", "ESCALATE" };
        var declared = Array.Empty<string>();

        var tools = AgentToolCatalog.FromStep(legalEvents, declared);

        Assert.Empty(tools);
        Assert.DoesNotContain(tools, t =>
            t.Kind == "event" ||
            legalEvents.Contains(t.Name, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void FromStep_IncludesDeclaredTools_ExcludesEventNameCollisions()
    {
        var legalEvents = new[] { "APPROVE", "REJECT" };
        // One declared tool shares a name with a legal event — it must be skipped.
        var declared = new[] { "lookup", "APPROVE" };

        var tools = AgentToolCatalog.FromStep(legalEvents, declared);

        // Only "lookup" should be in the tool list; "APPROVE" collides with an event.
        Assert.Single(tools);
        Assert.Equal("lookup", tools[0].Name, ignoreCase: true);
    }

    [Fact]
    public void FromStep_WithOnlyDeclaredTools_NoEventNames_ReturnsAllTools()
    {
        var legalEvents = new[] { "APPROVE" };
        var declared = new[] { "connector:crm.read", "LookupRecord:policy" };

        var tools = AgentToolCatalog.FromStep(legalEvents, declared);

        Assert.Equal(2, tools.Count);
        Assert.DoesNotContain(tools, t => t.Name == "APPROVE");
    }

    [Fact]
    public void FromStep_EmptyEverything_ReturnsEmpty()
    {
        var tools = AgentToolCatalog.FromStep(
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.Empty(tools);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// OpenAI adapter: structured output (response_format) is applied only when
// there are no real tools — now that events are not tools, a step with only
// legal events and no declared tools must get structured output.
// ─────────────────────────────────────────────────────────────────────────────
public class OpenAiStructuredOutputWithNoRealToolsTests
{
    [Fact]
    public async Task CreateRequest_NoTools_AppliesResponseFormat()
    {
        var adapter = new OpenAiProviderAdapter(useStrictJsonSchema: true);
        var request = adapter.CreateRequest(
            "", "sk-test", "gpt-4o-mini",
            "system", "packet",
            tools: null); // no declared tools

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("tools", out _),
            "No tools should be sent when the tool list is empty.");
        Assert.True(root.TryGetProperty("response_format", out _),
            "response_format must be set when there are no tools.");
    }

    [Fact]
    public async Task CreateRequest_WithTools_DoesNotApplyResponseFormat()
    {
        var adapter = new OpenAiProviderAdapter(useStrictJsonSchema: true);
        var tools = new List<AgentToolDescriptor>
        {
            new("lookup", "resource", "LookupRecord", "Desc", "none", false, null)
        };

        var request = adapter.CreateRequest(
            "", "sk-test", "gpt-4o-mini",
            "system", "packet",
            tools: tools);

        var body = await request.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("tools", out _));
        Assert.False(root.TryGetProperty("response_format", out _),
            "response_format must NOT be set when tools are present.");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Shared test infrastructure
// ─────────────────────────────────────────────────────────────────────────────
internal sealed class SequencedStubHandler : HttpMessageHandler
{
    private readonly Queue<string> _responses;
    private readonly Func<HttpRequestMessage, Task>? _onRequest;
    private readonly string? _subsequent;

    public SequencedStubHandler(string response, Func<HttpRequestMessage, Task>? onRequest = null)
        : this(new[] { response }, onRequest) { }

    public SequencedStubHandler(string[] responses,
        Func<HttpRequestMessage, Task>? onRequest = null)
    {
        _responses = new Queue<string>(responses);
        _onRequest = onRequest;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_onRequest != null)
            await _onRequest(request);

        var body = _responses.Count > 0
            ? _responses.Dequeue()
            : """{"choices":[{"message":{"content":"{\"insight\":\"ok\",\"actions\":[]}"}}]}""";

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
