using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Agents.Abstractions;
using FlowOS.Infrastructure.Persistence.Repositories;
using FlowOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOS.UnitTests.Agents;

public class ConversationStoreTests
{
    private static FlowOSDbContext MakeDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new FlowOSDbContext(options);
    }

    [Fact]
    public async Task AppendMessageAsync_Saves_Message()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = MakeDbContext(dbName);
        var store = new ConversationStore(db);

        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var stepId = "step1";
        var message = new ChatMessage(ChatMessageRole.User, "Hello");

        await store.AppendMessageAsync(tenantId, instanceId, stepId, message);

        var history = await store.GetHistoryAsync(tenantId, instanceId, stepId);
        Assert.Equal(1, history.Count);
        Assert.Equal("Hello", history[0].Content);
        Assert.Equal(ChatMessageRole.User, history[0].Role);
    }

    [Fact]
    public async Task AppendMessagesAsync_Saves_Multiple()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = MakeDbContext(dbName);
        var store = new ConversationStore(db);

        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var stepId = "step1";
        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var messages = new List<ChatMessage>
        {
            new(ChatMessageRole.User, "Hi", null, baseTime),
            new(ChatMessageRole.Assistant, "Hello!", null, baseTime.AddMinutes(1)),
            new(ChatMessageRole.User, "How are you?", null, baseTime.AddMinutes(2))
        };

        await store.AppendMessagesAsync(tenantId, instanceId, stepId, messages);

        var history = await store.GetHistoryAsync(tenantId, instanceId, stepId);
        Assert.Equal(3, history.Count);
        Assert.Equal("Hi", history[0].Content);
        Assert.Equal(ChatMessageRole.User, history[0].Role);
        Assert.Equal("Hello!", history[1].Content);
        Assert.Equal(ChatMessageRole.Assistant, history[1].Role);
        Assert.Equal("How are you?", history[2].Content);
        Assert.Equal(ChatMessageRole.User, history[2].Role);
    }

    [Fact]
    public async Task GetHistoryAsync_OnlyReturns_MatchingTenant_Instance_Step()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = MakeDbContext(dbName);
        var store = new ConversationStore(db);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var instanceX = Guid.NewGuid();
        var stepS1 = "S1";
        var stepS2 = "S2";

        await store.AppendMessageAsync(tenantA, instanceX, stepS1,
            new ChatMessage(ChatMessageRole.User, "A-X-S1"));
        await store.AppendMessageAsync(tenantA, instanceX, stepS2,
            new ChatMessage(ChatMessageRole.User, "A-X-S2"));
        await store.AppendMessageAsync(tenantB, instanceX, stepS1,
            new ChatMessage(ChatMessageRole.User, "B-X-S1"));

        var history = await store.GetHistoryAsync(tenantA, instanceX, stepS1);
        Assert.Equal(1, history.Count);
        Assert.Equal("A-X-S1", history[0].Content);
    }

    [Fact]
    public async Task ListSessionsAsync_Returns_DistinctSessions_OrderedByLatest()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = MakeDbContext(dbName);
        var store = new ConversationStore(db);

        var tenantT = Guid.NewGuid();
        var instance1 = Guid.NewGuid();
        var instance2 = Guid.NewGuid();
        var stepA = "A";
        var stepB = "B";
        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await store.AppendMessageAsync(tenantT, instance1, stepA,
            new ChatMessage(ChatMessageRole.User, "msg1", null, baseTime));
        await store.AppendMessageAsync(tenantT, instance1, stepA,
            new ChatMessage(ChatMessageRole.Assistant, "msg2", null, baseTime.AddHours(2)));
        await store.AppendMessageAsync(tenantT, instance2, stepB,
            new ChatMessage(ChatMessageRole.User, "msg3", null, baseTime.AddHours(1)));

        var sessions = await store.ListSessionsAsync(tenantT, 10);

        Assert.Equal(2, sessions.Count);
        Assert.Equal(instance1, sessions[0].WorkflowInstanceId);
        Assert.Equal(stepA, sessions[0].StepId);
        Assert.Equal(2, sessions[0].MessageCount);
        Assert.Equal(instance2, sessions[1].WorkflowInstanceId);
        Assert.Equal(stepB, sessions[1].StepId);
        Assert.Equal(1, sessions[1].MessageCount);
    }

    [Fact]
    public async Task ListSessionsAsync_DoesNotInclude_OtherTenants()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = MakeDbContext(dbName);
        var store = new ConversationStore(db);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await store.AppendMessageAsync(tenantA, Guid.NewGuid(), "S1",
            new ChatMessage(ChatMessageRole.User, "A1"));
        await store.AppendMessageAsync(tenantA, Guid.NewGuid(), "S2",
            new ChatMessage(ChatMessageRole.User, "A2"));
        await store.AppendMessageAsync(tenantB, Guid.NewGuid(), "S3",
            new ChatMessage(ChatMessageRole.User, "B1"));

        var sessions = await store.ListSessionsAsync(tenantA, 10);
        Assert.Equal(2, sessions.Count);
    }

    [Fact]
    public async Task ListSessionsAsync_Respects_Limit()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = MakeDbContext(dbName);
        var store = new ConversationStore(db);

        var tenantT = Guid.NewGuid();
        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 5; i++)
        {
            await store.AppendMessageAsync(tenantT, Guid.NewGuid(), $"S{i}",
                new ChatMessage(ChatMessageRole.User, $"msg{i}", null, baseTime.AddHours(i)));
        }

        var sessions = await store.ListSessionsAsync(tenantT, 2);

        Assert.Equal(2, sessions.Count);
        Assert.Equal("S4", sessions[0].StepId);
        Assert.Equal("S3", sessions[1].StepId);
    }

    [Fact]
    public async Task History_Timestamp_Order_Is_Ascending()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = MakeDbContext(dbName);
        var store = new ConversationStore(db);

        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var stepId = "step1";
        var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddMinutes(5);
        var t2 = t0.AddMinutes(2);

        await store.AppendMessageAsync(tenantId, instanceId, stepId,
            new ChatMessage(ChatMessageRole.User, "t0", null, t0));
        await store.AppendMessageAsync(tenantId, instanceId, stepId,
            new ChatMessage(ChatMessageRole.User, "t1", null, t1));
        await store.AppendMessageAsync(tenantId, instanceId, stepId,
            new ChatMessage(ChatMessageRole.User, "t2", null, t2));

        var history = await store.GetHistoryAsync(tenantId, instanceId, stepId);

        Assert.Equal(3, history.Count);
        Assert.Equal("t0", history[0].Content);
        Assert.Equal("t2", history[1].Content);
        Assert.Equal("t1", history[2].Content);
    }
}
