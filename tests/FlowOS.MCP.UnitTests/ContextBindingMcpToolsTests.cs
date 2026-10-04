using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using MediatR;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.MCP.UnitTests;

public sealed class ContextBindingMcpToolsTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IWorkflowContextSnapshotRepository> _snapshotRepoMock = new();

    public ContextBindingMcpToolsTests()
    {
        _unitOfWorkMock.Setup(u => u.WorkflowContextSnapshots).Returns(_snapshotRepoMock.Object);
    }

    [Fact]
    public async Task InspectContextSchema_WithDeclarativeSchema_ReturnsParsedMetadata()
    {
        var tools = new ContextBindingMcpTools(_mediatorMock.Object, _unitOfWorkMock.Object);

        var schemaJson = @"{
            ""entityType"": ""order"",
            ""entityIdField"": ""orderId"",
            ""fields"": {
                ""orderId"": { ""type"": ""string"", ""required"": true, ""immutable"": true },
                ""totalAmount"": { ""type"": ""number"", ""required"": true },
                ""retryCount"": { ""type"": ""counter"", ""default"": 0 }
            },
            ""computed"": {
                ""isHighValue"": { ""type"": ""boolean"", ""expression"": ""totalAmount > 1000"" }
            }
        }";

        var request = JObject.FromObject(new
        {
            schema = JObject.Parse(schemaJson)
        });

        var result = await tools.InspectContextSchema(request);

        Assert.False(result.IsError);
        var envelope = JObject.Parse(result.Content.Single().Text);
        var data = envelope["data"];
        Assert.NotNull(data);
        Assert.True(data["hasSchema"]?.Value<bool>());
        Assert.True(data["isDeclarative"]?.Value<bool>());
        Assert.NotNull(data["parsedSchema"]);
        Assert.Equal("order", data["parsedSchema"]?["entityType"]?.ToString());
        Assert.Equal("orderId", data["parsedSchema"]?["entityIdField"]?.ToString());
    }

    [Fact]
    public async Task InspectContextSchema_WithoutSchemaOrBinding_ReturnsHasSchemaFalse()
    {
        var tools = new ContextBindingMcpTools(_mediatorMock.Object, _unitOfWorkMock.Object);

        var request = new JObject();
        var result = await tools.InspectContextSchema(request);

        Assert.False(result.IsError);
        var envelope = JObject.Parse(result.Content.Single().Text);
        var data = envelope["data"];
        Assert.NotNull(data);
        Assert.False(data["hasSchema"]?.Value<bool>());
    }

    [Fact]
    public async Task TestContextOperations_AppliesAtomicOperationsCorrectly()
    {
        var tools = new ContextBindingMcpTools(_mediatorMock.Object, _unitOfWorkMock.Object);

        var schemaJson = @"{
            ""entityType"": ""order"",
            ""entityIdField"": ""orderId"",
            ""fields"": {
                ""orderId"": { ""type"": ""string"", ""required"": true, ""immutable"": true },
                ""status"": { ""type"": ""string"" },
                ""retryCount"": { ""type"": ""counter"", ""default"": 0 },
                ""items"": { ""type"": ""array"", ""appendOnly"": true }
            }
        }";

        var request = JObject.FromObject(new
        {
            schema = JObject.Parse(schemaJson),
            baseContext = new
            {
                orderId = "ORD-99",
                status = "draft",
                retryCount = 0,
                items = new[] { "item-1" }
            },
            operations = new object[]
            {
                new { op = "Set", field = "status", value = "submitted" },
                new { op = "Increment", field = "retryCount", value = 1 },
                new { op = "Append", field = "items", value = "item-2" }
            },
            actor = "TestRunner"
        });

        var result = await tools.TestContextOperations(request);

        Assert.False(result.IsError);
        var envelope = JObject.Parse(result.Content.Single().Text);
        var data = envelope["data"];
        Assert.NotNull(data);
        Assert.True(data["success"]?.Value<bool>());
        Assert.Equal("order", data["entityType"]?.ToString());
        Assert.Equal("ORD-99", data["entityId"]?.ToString());

        var canonical = data["canonicalData"];
        Assert.NotNull(canonical);
        Assert.Equal("submitted", canonical["status"]?.ToString());
        Assert.Equal(1, canonical["retryCount"]?.Value<int>());
        var items = canonical["items"] as JArray;
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        Assert.Equal("item-1", items[0].ToString());
        Assert.Equal("item-2", items[1].ToString());
    }

    [Fact]
    public async Task TestContextOperations_ViolatingImmutability_ReturnsError()
    {
        var tools = new ContextBindingMcpTools(_mediatorMock.Object, _unitOfWorkMock.Object);

        var schemaJson = @"{
            ""fields"": {
                ""orderId"": { ""type"": ""string"", ""immutable"": true }
            }
        }";

        var request = JObject.FromObject(new
        {
            schema = JObject.Parse(schemaJson),
            baseContext = new { orderId = "ORD-ORIGINAL" },
            operations = new object[]
            {
                new { op = "Set", field = "orderId", value = "ORD-MODIFIED" }
            }
        });

        var result = await tools.TestContextOperations(request);

        Assert.True(result.IsError);
        var envelope = JObject.Parse(result.Content.Single().Text);
        Assert.Equal("CTX-STATE-001", envelope["errorCode"]?.ToString());
    }

    [Fact]
    public async Task InspectInstanceContext_WhenSnapshotExists_ReturnsFullPartitions()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var instanceId = Guid.NewGuid();
            var revisionId = Guid.NewGuid();

            var initialData = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["orderId"] = JsonSerializer.SerializeToElement("ORD-123"),
                ["amount"] = JsonSerializer.SerializeToElement(500)
            };

            var businessRef = new WorkflowBusinessReference { SourceSystem = "ERP", ExternalEntityId = "EXT-999" };
            var snapshot = new WorkflowContextSnapshot(
                instanceId,
                tenantId,
                revisionId,
                initialData,
                businessReference: businessRef);

            _snapshotRepoMock.Setup(r => r.GetAsync(instanceId, tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(snapshot);

            var tools = new ContextBindingMcpTools(_mediatorMock.Object, _unitOfWorkMock.Object);

            var request = JObject.FromObject(new
            {
                tenantId,
                workflowInstanceId = instanceId
            });

            var result = await tools.InspectInstanceContext(request);

            Assert.False(result.IsError);
            var envelope = JObject.Parse(result.Content.Single().Text);
            var data = envelope["data"];
            Assert.NotNull(data);
            Assert.Equal(instanceId.ToString(), data["workflowInstanceId"]?.ToString());
            Assert.Equal(tenantId.ToString(), data["tenantId"]?.ToString());

            var identity = data["identity"];
            Assert.NotNull(identity);
            Assert.Equal("ERP", identity["sourceSystem"]?.ToString());

            var businessData = data["businessData"];
            Assert.NotNull(businessData);
            Assert.Equal("ORD-123", businessData["orderId"]?.ToString());
            Assert.Equal(500, businessData["amount"]?.Value<int>());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task InspectInstanceContext_WhenSnapshotNotFound_ReturnsNotFoundError()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var instanceId = Guid.NewGuid();

            _snapshotRepoMock.Setup(r => r.GetAsync(instanceId, tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((WorkflowContextSnapshot?)null);

            var tools = new ContextBindingMcpTools(_mediatorMock.Object, _unitOfWorkMock.Object);

            var request = JObject.FromObject(new
            {
                tenantId,
                workflowInstanceId = instanceId
            });

            var result = await tools.InspectInstanceContext(request);

            Assert.True(result.IsError);
            var envelope = JObject.Parse(result.Content.Single().Text);
            Assert.Equal("MCP-NOTFOUND-001", envelope["errorCode"]?.ToString());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }
}
