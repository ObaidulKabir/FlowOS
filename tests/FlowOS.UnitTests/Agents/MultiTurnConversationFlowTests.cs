using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Agents.Abstractions;

namespace FlowOS.UnitTests.Agents;

public class MultiTurnConversationFlowTests
{
    private static DecisionPacket CreateTestPacket(Guid tenantId, Guid instanceId, string stepId)
    {
        return new DecisionPacket(
            tenantId,
            instanceId,
            stepId,
            "Active",
            "ManualAction",
            "user",
            null,
            null,
            new Dictionary<string, object?>(),
            new List<string>(),
            new List<string>(),
            new List<string>(),
            new List<SlaReminderFact>(),
            null,
            new Dictionary<string, object>(),
            "Test objective",
            null);
    }

    private static async Task RunOrchestrationSnippet(
        IConversationStore store,
        IConversationalAgent agent,
        Guid tenantId,
        Guid instanceId,
        string stepId,
        DecisionPacket packet,
        CancellationToken ct)
    {
        var history = await store.GetHistoryAsync(tenantId, instanceId, stepId, ct);
        var result = await agent.ExecuteConversationalAsync(packet, history, ct);
        if (result.Success && !string.IsNullOrWhiteSpace(result.Insight))
        {
            await store.AppendMessageAsync(
                tenantId,
                instanceId,
                stepId,
                new ChatMessage(ChatMessageRole.Assistant, result.Insight, null, DateTimeOffset.UtcNow),
                ct);
        }
    }

    [Fact]
    public async Task When_ConversationalAgent_Succeeds_AssistantMessage_Is_Appended_To_Store()
    {
        var store = new FakeConversationStore();
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var stepId = "step1";
        var ct = CancellationToken.None;
        var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await store.AppendMessageAsync(tenantId, instanceId, stepId,
            new ChatMessage(ChatMessageRole.User, "Please help me", null, t0));

        var agent = new ConversationalFixedAgent(
            AgentResult.FromInsight("Understood! Let's proceed."));

        var packet = CreateTestPacket(tenantId, instanceId, stepId);

        await RunOrchestrationSnippet(store, agent, tenantId, instanceId, stepId, packet, ct);

        var history = await store.GetHistoryAsync(tenantId, instanceId, stepId, ct);
        Assert.Equal(2, history.Count);
        Assert.Equal(ChatMessageRole.Assistant, history[1].Role);
        Assert.Equal("Understood! Let's proceed.", history[1].Content);
    }

    [Fact]
    public async Task When_Agent_Fails_No_Assistant_Message_Appended()
    {
        var store = new FakeConversationStore();
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var stepId = "step1";
        var ct = CancellationToken.None;

        await store.AppendMessageAsync(tenantId, instanceId, stepId,
            new ChatMessage(ChatMessageRole.User, "Please help me"));

        var agent = new ConversationalFixedAgent(
            AgentResult.Failure(AgentFailureCodes.ProviderRateLimit, "fail"));

        var packet = CreateTestPacket(tenantId, instanceId, stepId);

        await RunOrchestrationSnippet(store, agent, tenantId, instanceId, stepId, packet, ct);

        var history = await store.GetHistoryAsync(tenantId, instanceId, stepId, ct);
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public async Task MultiTurn_History_Is_Available_To_Agent_On_Second_Turn()
    {
        var store = new FakeConversationStore();
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var stepId = "step1";
        var ct = CancellationToken.None;
        var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(1);

        await store.AppendMessageAsync(tenantId, instanceId, stepId,
            new ChatMessage(ChatMessageRole.User, "what is 2+2?", null, t0));
        await store.AppendMessageAsync(tenantId, instanceId, stepId,
            new ChatMessage(ChatMessageRole.Assistant, "4", null, t1));

        IReadOnlyList<ChatMessage>? capturedHistory = null;
        var agent = new MultiTurnRecordingAgent(
            AgentResult.FromInsight("ok"),
            h => capturedHistory = h);

        var packet = CreateTestPacket(tenantId, instanceId, stepId);

        await RunOrchestrationSnippet(store, agent, tenantId, instanceId, stepId, packet, ct);

        Assert.NotNull(capturedHistory);
        Assert.Equal(2, capturedHistory.Count);
        Assert.Equal(ChatMessageRole.User, capturedHistory[0].Role);
        Assert.Equal("what is 2+2?", capturedHistory[0].Content);
        Assert.Equal(ChatMessageRole.Assistant, capturedHistory[1].Role);
        Assert.Equal("4", capturedHistory[1].Content);
    }
}

public class FakeConversationStore : IConversationStore
{
    private readonly List<(Guid TenantId, Guid InstanceId, string StepId, ChatMessage Message)> _messages = new();

    private static ChatMessage Normalize(ChatMessage m)
    {
        if (m.Timestamp.HasValue) return m;
        return m with { Timestamp = DateTimeOffset.UtcNow };
    }

    public Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        CancellationToken cancellationToken = default)
    {
        var result = _messages
            .Where(m => m.TenantId == tenantId && m.InstanceId == workflowInstanceId && m.StepId == stepId)
            .Select(m => m.Message)
            .OrderBy(m => m.Timestamp!.Value)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<ChatMessage>>(result);
    }

    public Task AppendMessageAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        _messages.Add((tenantId, workflowInstanceId, stepId, Normalize(message)));
        return Task.CompletedTask;
    }

    public Task AppendMessagesAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        foreach (var m in messages)
        {
            _messages.Add((tenantId, workflowInstanceId, stepId, Normalize(m)));
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConversationSessionInfo>> ListSessionsAsync(
        Guid tenantId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var sessions = _messages
            .Where(m => m.TenantId == tenantId)
            .GroupBy(m => new { m.InstanceId, m.StepId })
            .Select(g => new ConversationSessionInfo(
                g.Key.InstanceId,
                g.Key.StepId,
                g.Count(),
                g.Max(m => m.Message.Timestamp!.Value)))
            .OrderByDescending(s => s.LastMessageAtUtc)
            .Take(limit)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<ConversationSessionInfo>>(sessions);
    }
}

public class MultiTurnRecordingAgent : IConversationalAgent
{
    private readonly AgentResult _result;
    private readonly Action<IReadOnlyList<ChatMessage>> _capture;

    public int MaxTurns => 10;

    public MultiTurnRecordingAgent(AgentResult result, Action<IReadOnlyList<ChatMessage>> capture)
    {
        _result = result;
        _capture = capture;
    }

    public Task<AgentResult> ExecuteAsync(AgentContext context)
    {
        return Task.FromResult(_result);
    }

    public Task<AgentResult> ExecuteConversationalAsync(
        DecisionPacket packet,
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        _capture(history);
        return Task.FromResult(_result);
    }
}
