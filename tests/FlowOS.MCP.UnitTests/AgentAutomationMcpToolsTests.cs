using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Infrastructure.Persistence;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using Microsoft.EntityFrameworkCore;
using Moq;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

public sealed class AgentAutomationMcpToolsTests
{
    [Fact]
    public async Task AppendAgentChatMessage_And_Read_Back_Conversation_Surface()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var workflowInstanceId = Guid.NewGuid();
            var sessionInfo = new ConversationSessionInfo(workflowInstanceId, "AgentReview", 1, DateTimeOffset.UtcNow);
            var conversationStore = new Mock<IConversationStore>();
            conversationStore
                .Setup(store => store.AppendMessageAsync(
                    tenantId,
                    workflowInstanceId,
                    "AgentReview",
                    It.IsAny<ChatMessage>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            conversationStore
                .Setup(store => store.GetHistoryAsync(
                    tenantId,
                    workflowInstanceId,
                    "AgentReview",
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new ChatMessage(ChatMessageRole.User, "Please re-check the quote.", "Olivia", DateTimeOffset.UtcNow)
                ]);
            conversationStore
                .Setup(store => store.ListSessionsAsync(tenantId, 50, It.IsAny<CancellationToken>()))
                .ReturnsAsync([sessionInfo]);

            var coordinator = new Mock<IAgentTaskCoordinator>();
            coordinator
                .Setup(service => service.EnqueueCurrentStepAsync(
                    tenantId,
                    workflowInstanceId,
                    AgentTaskSource.McpRequest,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new AgentTaskEnqueueResult(Guid.NewGuid(), true, AgentTaskJobStatus.Pending, "active-1")
                ]);

            await using var db = CreateDb($"agent-automation-chat-{tenantId:N}");
            var tools = new AgentAutomationMcpTools(conversationStore.Object, coordinator.Object, db);

            var append = await tools.AppendAgentChatMessage(JObject.FromObject(new
            {
                tenantId,
                workflowInstanceId,
                stepId = "AgentReview",
                message = "Please re-check the quote.",
                name = "Olivia",
                triggerAgent = true
            }));

            Assert.False(append.IsError);
            var appendJson = JObject.Parse(append.Content.Single().Text);
            Assert.True(appendJson["data"]!["messageRecorded"]!.Value<bool>());
            Assert.Equal(1, appendJson["data"]!["enqueuedJobs"]!.Count());

            var history = await tools.GetAgentChatHistory(JObject.FromObject(new
            {
                tenantId,
                workflowInstanceId,
                stepId = "AgentReview"
            }));

            Assert.False(history.IsError);
            var historyJson = JObject.Parse(history.Content.Single().Text);
            Assert.Equal(1, historyJson["data"]!["count"]!.Value<int>());
            Assert.Equal("User", historyJson["data"]!["messages"]![0]!["role"]?.ToString());

            var sessions = await tools.ListAgentChatSessions(JObject.FromObject(new { tenantId, limit = 50 }));
            Assert.False(sessions.IsError);
            var sessionsJson = JObject.Parse(sessions.Content.Single().Text);
            Assert.Equal(1, sessionsJson["data"]!["totalCount"]!.Value<int>());
            Assert.Equal("AgentReview", sessionsJson["data"]!["sessions"]![0]!["stepId"]?.ToString());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task ListAgentPromptAudits_Respects_Payload_Visibility_Flag()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            await using var db = CreateDb($"agent-automation-audits-{tenantId:N}");
            db.Tenants.Add(new Tenant("Audit Tenant"));
            db.AgentPromptAuditRecords.Add(new AgentPromptAuditRecord(
                Guid.NewGuid(),
                tenantId,
                "openai",
                "gpt-4o-mini",
                DateTime.UtcNow,
                "{\"prompt\":\"hello\"}",
                120,
                workflowInstanceId: Guid.NewGuid(),
                stepId: "AgentReview",
                providerAlias: "quote-llm",
                rawResponsePayload: "{\"response\":\"hi\"}",
                executionId: Guid.NewGuid()));
            await db.SaveChangesAsync();

            var tools = new AgentAutomationMcpTools(
                Mock.Of<IConversationStore>(),
                Mock.Of<IAgentTaskCoordinator>(),
                db);

            var withoutPayloads = await tools.ListAgentPromptAudits(JObject.FromObject(new
            {
                tenantId,
                limit = 10,
                includePayloads = false
            }));

            Assert.False(withoutPayloads.IsError);
            var withoutJson = JObject.Parse(withoutPayloads.Content.Single().Text);
            Assert.Equal(1, withoutJson["data"]!["totalCount"]!.Value<int>());
            Assert.True(withoutJson["data"]!["audits"]![0]!["rawRequestPayload"]?.Type is JTokenType.Null or JTokenType.Undefined);

            var withPayloads = await tools.ListAgentPromptAudits(JObject.FromObject(new
            {
                tenantId,
                limit = 10,
                includePayloads = true
            }));

            Assert.False(withPayloads.IsError);
            var withJson = JObject.Parse(withPayloads.Content.Single().Text);
            Assert.Equal("{\"prompt\":\"hello\"}", withJson["data"]!["audits"]![0]!["rawRequestPayload"]?.ToString());
            Assert.Equal("{\"response\":\"hi\"}", withJson["data"]!["audits"]![0]!["rawResponsePayload"]?.ToString());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task ConfigureExternalAgentSettings_RoundTrips_Through_Mcp()
    {
        McpRequestContext.Clear();
        try
        {
            var tenant = new Tenant("External Agent Tenant");
            await using var db = CreateDb($"agent-automation-ext-{tenant.TenantId:N}");
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            var tools = new AgentAutomationMcpTools(
                Mock.Of<IConversationStore>(),
                Mock.Of<IAgentTaskCoordinator>(),
                db);

            var update = await tools.ConfigureExternalAgentSettings(JObject.FromObject(new
            {
                tenantId = tenant.TenantId,
                enabled = true,
                autoPilot = true,
                agentProfileId = "ops-planner"
            }));

            Assert.False(update.IsError);
            var updateJson = JObject.Parse(update.Content.Single().Text);
            Assert.True(updateJson["data"]!["enabled"]!.Value<bool>());
            Assert.True(updateJson["data"]!["autoPilot"]!.Value<bool>());
            Assert.Equal("ops-planner", updateJson["data"]!["agentProfileId"]?.ToString());

            var loaded = await tools.GetExternalAgentSettings(JObject.FromObject(new
            {
                tenantId = tenant.TenantId
            }));

            Assert.False(loaded.IsError);
            var loadedJson = JObject.Parse(loaded.Content.Single().Text);
            Assert.True(loadedJson["data"]!["enabled"]!.Value<bool>());
            Assert.True(loadedJson["data"]!["autoPilot"]!.Value<bool>());
            Assert.Equal("ops-planner", loadedJson["data"]!["agentProfileId"]?.ToString());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    private static FlowOSDbContext CreateDb(string databaseName)
        => new(new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);
}
