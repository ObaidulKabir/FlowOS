using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Core.Common.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.MCP.Models;
using FlowOS.MCP.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class DeadLetterAndBackoffTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public void OutboxMessage_CalculatesExponentialBackoff_Correctly()
    {
        var msg = new OutboxMessage(_tenantId, "WorkflowAction:Webhook", "{\"url\":\"https://api.example.com\"}");
        Assert.Null(msg.NextRetryUtc);
        Assert.False(msg.IsDeadLetter);
        Assert.Equal(0, msg.RetryCount);

        var before = DateTime.UtcNow;

        // Retry 1: 2^(1-1) * 2 = 2s
        msg.RecordFailure("HTTP 504 Gateway Timeout", maxRetries: 5, baseDelaySeconds: 2);
        Assert.Equal(1, msg.RetryCount);
        Assert.False(msg.IsDeadLetter);
        Assert.NotNull(msg.NextRetryUtc);
        Assert.True(msg.NextRetryUtc.Value >= before.AddSeconds(1.8) && msg.NextRetryUtc.Value <= before.AddSeconds(5));

        // Retry 2: 2^(2-1) * 2 = 4s
        msg.RecordFailure("HTTP 504 Gateway Timeout", maxRetries: 5, baseDelaySeconds: 2);
        Assert.Equal(2, msg.RetryCount);
        Assert.False(msg.IsDeadLetter);
        Assert.True(msg.NextRetryUtc.Value >= before.AddSeconds(3.8));

        // Retry 3: 2^(3-1) * 2 = 8s
        msg.RecordFailure("HTTP 504 Gateway Timeout", maxRetries: 5, baseDelaySeconds: 2);
        Assert.Equal(3, msg.RetryCount);
        Assert.False(msg.IsDeadLetter);
        Assert.True(msg.NextRetryUtc.Value >= before.AddSeconds(7.8));

        // Retry 4: 2^(4-1) * 2 = 16s
        msg.RecordFailure("HTTP 504 Gateway Timeout", maxRetries: 5, baseDelaySeconds: 2);
        Assert.Equal(4, msg.RetryCount);
        Assert.False(msg.IsDeadLetter);
        Assert.True(msg.NextRetryUtc.Value >= before.AddSeconds(15.8));

        // Retry 5: Reaches MaxRetries (5) -> Marked as Dead Letter
        msg.RecordFailure("HTTP 504 Gateway Timeout", maxRetries: 5, baseDelaySeconds: 2);
        Assert.Equal(5, msg.RetryCount);
        Assert.True(msg.IsDeadLetter);
        Assert.Null(msg.NextRetryUtc);
    }

    [Fact]
    public void OutboxMessage_ReplayFromDeadLetter_ResetsState()
    {
        var msg = new OutboxMessage(_tenantId, "WorkflowAction:Webhook", "{}");
        for (int i = 0; i < 5; i++)
        {
            msg.RecordFailure("Fatal error", maxRetries: 5);
        }

        Assert.True(msg.IsDeadLetter);
        Assert.Equal(5, msg.RetryCount);
        Assert.NotNull(msg.Error);

        msg.ReplayFromDeadLetter();

        Assert.False(msg.IsDeadLetter);
        Assert.Equal(0, msg.RetryCount);
        Assert.Null(msg.Error);
        Assert.Null(msg.ProcessedOnUtc);
        Assert.NotNull(msg.NextRetryUtc);
        Assert.True(msg.NextRetryUtc.Value <= DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task DeadLetterService_CanList_Retry_AndPurge_DeadLetters()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase("FlowOS_DLQ_Test_" + Guid.NewGuid())
            .Options;

        using var db = new FlowOSDbContext(options);
        var service = new DeadLetterService(db, NullLogger<DeadLetterService>.Instance);

        var msg1 = new OutboxMessage(_tenantId, "WorkflowAction:Webhook", "{\"actionType\":\"Webhook\",\"url\":\"https://partner.com/api\",\"method\":\"POST\",\"stepId\":\"DispatchStep\"}");
        for (int i = 0; i < 5; i++) msg1.RecordFailure("Connection refused");

        var msg2 = new OutboxMessage(_tenantId, "OrderCreatedEvent", "{\"orderId\":\"123\"}");
        for (int i = 0; i < 5; i++) msg2.RecordFailure("Service unavailable");

        db.OutboxMessages.AddRange(msg1, msg2);
        await db.SaveChangesAsync();

        // 1. List dead letters
        var deadLetters = await service.GetDeadLettersAsync(_tenantId);
        Assert.Equal(2, deadLetters.Count);
        var webhookItem = deadLetters.FirstOrDefault(d => d.Id == msg1.Id);
        Assert.NotNull(webhookItem);
        Assert.Equal("Webhook", webhookItem!.ActionType);
        Assert.Equal("https://partner.com/api", webhookItem.TargetUrl);
        Assert.Equal("DispatchStep", webhookItem.StepId);
        Assert.True(webhookItem.IsDeadLetter);
        Assert.Equal("Connection refused", webhookItem.Error);

        // 2. Retry single dead letter
        var retryOk = await service.RetryDeadLetterAsync(msg1.Id, _tenantId);
        Assert.True(retryOk);

        var refreshedMsg1 = await db.OutboxMessages.FindAsync(msg1.Id);
        Assert.NotNull(refreshedMsg1);
        Assert.False(refreshedMsg1!.IsDeadLetter);
        Assert.Equal(0, refreshedMsg1.RetryCount);

        // 3. Purge dead letter
        var purgeOk = await service.PurgeDeadLetterAsync(msg2.Id, _tenantId);
        Assert.True(purgeOk);

        var refreshedMsg2 = await db.OutboxMessages.FindAsync(msg2.Id);
        Assert.Null(refreshedMsg2);
    }

    [Fact]
    public async Task DeadLetterMcpTools_CanList_Retry_AndPurge_ViaMcp()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase("FlowOS_DLQ_MCP_Test_" + Guid.NewGuid())
            .Options;

        using var db = new FlowOSDbContext(options);
        var service = new DeadLetterService(db, NullLogger<DeadLetterService>.Instance);
        var mcpTools = new DeadLetterMcpTools(service);

        var deadMsg = new OutboxMessage(_tenantId, "WorkflowAction:Webhook", "{\"actionType\":\"Webhook\",\"url\":\"https://orders.io/api\",\"method\":\"POST\",\"stepId\":\"ShipStep\"}");
        for (int i = 0; i < 5; i++) deadMsg.RecordFailure("503 Service Unavailable");
        db.OutboxMessages.Add(deadMsg);
        await db.SaveChangesAsync();

        // Test list_dead_letters
        var listResult = await mcpTools.ListDeadLetters(JObject.Parse($$"""
        {
          "tenantId": "{{_tenantId}}",
          "limit": 10
        }
        """));

        Assert.False(listResult.IsError);
        var listOutput = JObject.Parse(listResult.Content[0].Text);
        var listData = listOutput["data"] as JObject;
        Assert.NotNull(listData);
        Assert.Equal(1, listData!["totalCount"]?.Value<int>());
        var items = listData["deadLetters"] as JArray;
        Assert.NotNull(items);
        Assert.Single(items!);
        Assert.Equal("Webhook", items![0]["actionType"]?.ToString());
        Assert.Equal("https://orders.io/api", items![0]["targetUrl"]?.ToString());

        // Test retry_dead_letter
        var retryResult = await mcpTools.RetryDeadLetter(JObject.Parse($$"""
        {
          "id": "{{deadMsg.Id}}",
          "tenantId": "{{_tenantId}}"
        }
        """));
        Assert.False(retryResult.IsError);

        var updated = await db.OutboxMessages.FindAsync(deadMsg.Id);
        Assert.NotNull(updated);
        Assert.False(updated!.IsDeadLetter);
        Assert.Equal(0, updated.RetryCount);

        // Test purge_dead_letter
        var purgeResult = await mcpTools.PurgeDeadLetter(JObject.Parse($$"""
        {
          "id": "{{deadMsg.Id}}",
          "tenantId": "{{_tenantId}}"
        }
        """));
        Assert.False(purgeResult.IsError);

        var deleted = await db.OutboxMessages.FindAsync(deadMsg.Id);
        Assert.Null(deleted);
    }
}
