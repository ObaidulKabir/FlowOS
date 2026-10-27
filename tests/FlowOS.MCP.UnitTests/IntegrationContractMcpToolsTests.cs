using FlowOS.Application.Commands;
using FlowOS.Application.Common;
using FlowOS.Application.Common.Exceptions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.DTOs;
using FlowOS.Application.Queries;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

/// <summary>
/// The MCP tools expose the Learning OS integration contract added to the HTTP API:
/// references on start, causation on publish, list by reference, retryable conflicts (CR-06/CR-10)
/// and the change feed (CR-10c).
/// </summary>
public class IntegrationContractMcpToolsTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public IntegrationContractMcpToolsTests()
    {
        McpRequestContext.TenantId = _tenantId;
    }

    public void Dispose() => McpRequestContext.Clear();

    private static JObject Payload(CallToolResult result) => JObject.Parse(result.Content.Single().Text!);

    [Fact]
    public async Task StartWorkflow_returns_the_pinned_definition_and_the_callers_reference()
    {
        var instanceId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<StartWorkflowCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(instanceId);
        mediator.Setup(m => m.Send(It.Is<GetWorkflowByIdQuery>(q => q.Id == instanceId && q.TenantId == _tenantId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowSummaryDto
            {
                Id = instanceId,
                DefinitionId = definitionId,
                Version = 3,
                Status = "Running",
                ExternalSourceSystem = "learning-os",
                ExternalReference = "recovery-case-123"
            });

        var result = await new ExecutionTools(mediator.Object).StartWorkflow(JObject.FromObject(new
        {
            tenantId = _tenantId,
            workflowName = "Recovery",
            businessReference = new { sourceSystem = "learning-os", externalEntityId = "recovery-case-123" }
        }));

        var data = Payload(result)["data"]!;
        Assert.False(result.IsError);
        Assert.Equal(instanceId, Guid.Parse(data["workflowInstanceId"]!.ToString()));
        Assert.Equal(definitionId, Guid.Parse(data["workflowDefinitionId"]!.ToString()));
        Assert.Equal(3, data["workflowVersion"]!.Value<int>());
        Assert.Equal("learning-os", data["externalSourceSystem"]!.Value<string>());
        Assert.Equal("recovery-case-123", data["externalReference"]!.Value<string>());
        mediator.Verify(m => m.Send(It.Is<StartWorkflowCommand>(c =>
            c.BusinessReference != null && c.BusinessReference.ExternalEntityId == "recovery-case-123"), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task StartWorkflow_still_succeeds_when_the_summary_cannot_be_read()
    {
        var instanceId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<StartWorkflowCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(instanceId);

        var result = await new ExecutionTools(mediator.Object).StartWorkflow(JObject.FromObject(new { tenantId = _tenantId, workflowName = "Recovery" }));

        var data = Payload(result)["data"]!;
        Assert.False(result.IsError);
        Assert.Equal("Running", data["status"]!.Value<string>());
        Assert.Equal(JTokenType.Null, data["externalReference"]!.Type);
    }

    [Fact]
    public async Task PublishEvent_passes_the_causation_id()
    {
        var causationId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<PublishEventCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await new ExecutionTools(mediator.Object).PublishEvent(JObject.FromObject(new
        {
            tenantId = _tenantId,
            workflowInstanceId = Guid.NewGuid(),
            eventType = "EVT-ASSESSED",
            causationId
        }));

        Assert.False(result.IsError, result.Content.Single().Text);
        mediator.Verify(m => m.Send(It.Is<PublishEventCommand>(c => c.CausationId == causationId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishEvent_rejects_a_causation_id_that_is_not_a_uuid()
    {
        var mediator = new Mock<IMediator>();

        var result = await new ExecutionTools(mediator.Object).PublishEvent(JObject.FromObject(new
        {
            tenantId = _tenantId,
            workflowInstanceId = Guid.NewGuid(),
            eventType = "EVT-ASSESSED",
            causationId = "decision-7"
        }));

        Assert.True(result.IsError);
        Assert.Equal("MCP-ARG-002", Payload(result)["errorCode"]!.Value<string>());
        mediator.Verify(m => m.Send(It.IsAny<PublishEventCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListWorkflowInstances_filters_by_the_callers_reference()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetWorkflowsQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<WorkflowSummaryDto>());

        await new ExecutionTools(mediator.Object).ListWorkflowInstances(JObject.FromObject(new
        {
            tenantId = _tenantId,
            sourceSystem = " learning-os ",
            externalReference = "recovery-case-123"
        }));
        await new ExecutionTools(mediator.Object).ListWorkflowInstances(JObject.FromObject(new { tenantId = _tenantId, sourceSystem = "  " }));

        mediator.Verify(m => m.Send(It.Is<GetWorkflowsQuery>(q =>
            q.TenantId == _tenantId && q.ExternalSourceSystem == "learning-os" && q.ExternalReference == "recovery-case-123"), It.IsAny<CancellationToken>()), Times.Once);
        mediator.Verify(m => m.Send(It.Is<GetWorkflowsQuery>(q =>
            q.ExternalSourceSystem == null && q.ExternalReference == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    public static IEnumerable<object[]> ConflictCodes() => new[]
    {
        new object[] { ConcurrencyConflictException.ConcurrentUpdate },
        new object[] { ConcurrencyConflictException.IdempotencyInProgress }
    };

    [Theory]
    [MemberData(nameof(ConflictCodes))]
    public async Task Conflicts_are_reported_as_retryable_on_start_publish_and_complete(string code)
    {
        var mediator = new Mock<IMediator>();
        var conflict = new ConcurrencyConflictException(code, "The record was changed by another request.");
        mediator.Setup(m => m.Send(It.IsAny<StartWorkflowCommand>(), It.IsAny<CancellationToken>())).ThrowsAsync(conflict);
        mediator.Setup(m => m.Send(It.IsAny<PublishEventCommand>(), It.IsAny<CancellationToken>())).ThrowsAsync(conflict);
        mediator.Setup(m => m.Send(It.IsAny<CompleteTaskCommand>(), It.IsAny<CancellationToken>())).ThrowsAsync(conflict);
        var tools = new ExecutionTools(mediator.Object);

        var results = new[]
        {
            await tools.StartWorkflow(JObject.FromObject(new { tenantId = _tenantId, workflowName = "Recovery" })),
            await tools.PublishEvent(JObject.FromObject(new { tenantId = _tenantId, workflowInstanceId = Guid.NewGuid(), eventType = "EVT-X" })),
            await tools.CompleteTask(JObject.FromObject(new { tenantId = _tenantId, workflowInstanceId = Guid.NewGuid(), taskId = Guid.NewGuid() }))
        };

        Assert.All(results, result =>
        {
            var payload = Payload(result);
            Assert.True(result.IsError);
            Assert.Equal("MCP-CONFLICT-001", payload["errorCode"]!.Value<string>());
            Assert.Equal(code, payload["context"]!["conflictCode"]!.Value<string>());
            Assert.True(payload["context"]!["retryable"]!.Value<bool>());
        });
    }

    private sealed class FakeChangeFeedReader : IChangeFeedReader
    {
        public Guid TenantId;
        public long After;
        public int Limit;
        public DateTime VisibleBefore;
        public ChangeFeedPage Page = new(Array.Empty<ChangeFeedItem>(), 0, false);

        public Task<ChangeFeedPage> ReadAsync(Guid tenantId, long afterSequence, int limit, DateTime visibleBeforeUtc, CancellationToken cancellationToken = default)
        {
            TenantId = tenantId;
            After = afterSequence;
            Limit = limit;
            VisibleBefore = visibleBeforeUtc;
            return Task.FromResult(Page);
        }
    }

    [Fact]
    public async Task ReadChangeFeed_uses_the_cursor_and_returns_the_v1_shape()
    {
        var instanceId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var reader = new FakeChangeFeedReader
        {
            Page = new ChangeFeedPage(new[]
            {
                new ChangeFeedItem(43, Guid.NewGuid(), "EVT-ASSESSED", 1, DateTime.UtcNow, instanceId, causationId, instanceId,
                    Guid.NewGuid(), 2, "learning-os", "recovery-case-123", "{\"score\":7}", new Dictionary<string, string> { ["ToStep"] = "END" })
            }, 43, true)
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FlowOS:ChangeFeed:SettleSeconds"] = "30" }).Build();

        var before = DateTime.UtcNow;
        var result = await new ChangeFeedMcpTools(reader, config).ReadChangeFeed(JObject.FromObject(new
        {
            tenantId = _tenantId,
            after = ChangeFeedCursor.Encode(42),
            limit = 10
        }));

        Assert.False(result.IsError, result.Content.Single().Text);
        Assert.Equal(_tenantId, reader.TenantId);
        Assert.Equal(42, reader.After);
        Assert.Equal(10, reader.Limit);
        Assert.InRange(reader.VisibleBefore, before.AddSeconds(-31), DateTime.UtcNow.AddSeconds(-29));

        var data = Payload(result)["data"]!;
        Assert.Equal("flowos.change-feed/v1", data["schema"]!.Value<string>());
        Assert.Equal(ChangeFeedCursor.Encode(43), data["nextCursor"]!.Value<string>());
        Assert.True(data["hasMore"]!.Value<bool>());
        var item = Assert.Single(data["items"]!.Children());
        Assert.Equal(43, item["sequence"]!.Value<long>());
        Assert.Equal(causationId, Guid.Parse(item["causationId"]!.ToString()));
        Assert.Equal("recovery-case-123", item["externalReference"]!.Value<string>());
        Assert.Equal(7, item["payload"]!["score"]!.Value<int>());
        Assert.Equal("END", item["metadata"]!["ToStep"]!.Value<string>());
    }

    [Theory]
    [InlineData("{\"after\":\"not-a-cursor\"}")]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":501}")]
    [InlineData("{\"limit\":\"ten\"}")]
    public async Task ReadChangeFeed_rejects_a_bad_cursor_or_limit(string json)
    {
        var reader = new FakeChangeFeedReader();
        var args = JObject.Parse(json);
        args["tenantId"] = _tenantId;

        var result = await new ChangeFeedMcpTools(reader).ReadChangeFeed(args);

        Assert.True(result.IsError);
        Assert.Equal("MCP-ARG-002", Payload(result)["errorCode"]!.Value<string>());
    }

    [Fact]
    public void Tool_catalog_documents_the_new_inputs_and_errors()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        using var services = new ServiceCollection().BuildServiceProvider();
        ToolRegistration.RegisterAll(registry, services);
        Assert.True(registry.Contains("read_change_feed"));

        Assert.Contains("causationId", McpToolSchemas.PublishEvent()["properties"]!.ToObject<JObject>()!.Properties().Select(p => p.Name));
        var listProps = McpToolSchemas.ListWorkflowInstances()["properties"]!.ToObject<JObject>()!.Properties().Select(p => p.Name).ToList();
        Assert.Contains("sourceSystem", listProps);
        Assert.Contains("externalReference", listProps);
        Assert.Contains("MCP-CONFLICT-001", McpToolDescriptions.For("start_workflow"));
        Assert.Contains("MCP-CONFLICT-001", McpToolDescriptions.For("publish_event"));
        Assert.Contains("MCP-CONFLICT-001", McpToolDescriptions.For("complete_task"));
        Assert.Contains("nextCursor", McpToolDescriptions.For("read_change_feed"));
        Assert.Equal("query", McpToolDescriptions.ProfileFor("read_change_feed").Category);
        Assert.Equal("none", McpToolDescriptions.ProfileFor("read_change_feed").SideEffect);
    }
}
