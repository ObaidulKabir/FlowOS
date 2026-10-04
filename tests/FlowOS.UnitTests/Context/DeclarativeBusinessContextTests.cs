using System;
using System.Collections.Generic;
using System.Text.Json;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Domain.Entities;
using FlowOS.Domain.ValueObjects;
using Xunit;

namespace FlowOS.UnitTests.Context;

public class DeclarativeBusinessContextTests
{
    private static readonly string SampleDeclarativeSchemaJson = @"
    {
      ""ContextSchema"": {
        ""entityType"": ""Order"",
        ""entityIdField"": ""orderId"",
        ""correlationFields"": [""customerId"", ""externalId""],
        ""fields"": {
          ""orderId"": { ""type"": ""string"", ""required"": true, ""immutable"": true },
          ""customerId"": { ""type"": ""string"", ""required"": true, ""immutable"": true },
          ""totalAmount"": { ""type"": ""number"", ""required"": true, ""min"": 0 },
          ""currency"": { ""type"": ""string"", ""default"": ""USD"" },
          ""retryCount"": { ""type"": ""counter"", ""default"": 0 },
          ""lineItems"": { ""type"": ""array"", ""appendOnly"": true },
          ""riskScore"": { ""type"": ""number"", ""computed"": true }
        },
        ""computed"": {
          ""riskScore"": {
            ""expression"": ""totalAmount > 5000 ? 0.8 : 0.2"",
            ""recomputeOn"": [""totalAmount""]
          }
        }
      }
    }";

    [Fact]
    public void ContextSchemaParser_Parses_Declarative_Schema_Correctly()
    {
        var success = ContextSchemaParser.TryParse(SampleDeclarativeSchemaJson, out var schema);

        Assert.True(success);
        Assert.NotNull(schema);
        Assert.Equal("Order", schema.EntityType);
        Assert.Equal("orderId", schema.EntityIdField);
        Assert.Equal(2, schema.CorrelationFields.Count);
        Assert.True(schema.Fields.ContainsKey("orderId"));
        Assert.True(schema.Fields["orderId"].Immutable);
        Assert.True(schema.Fields["lineItems"].AppendOnly);
        Assert.True(schema.Computed.ContainsKey("riskScore"));
    }

    [Fact]
    public void ContextSnapshot_Initializes_Identity_From_Schema()
    {
        ContextSchemaParser.TryParse(SampleDeclarativeSchemaJson, out var schema);

        var initialData = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["orderId"] = JsonSerializer.SerializeToElement("ORD-999"),
            ["customerId"] = JsonSerializer.SerializeToElement("CUST-100"),
            ["totalAmount"] = JsonSerializer.SerializeToElement(250.0)
        };

        var snapshot = new WorkflowContextSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            initialData,
            schema: schema);

        Assert.Equal("Order", snapshot.EntityType);
        Assert.Equal("ORD-999", snapshot.EntityId);
        Assert.Equal("CUST-100", snapshot.CorrelationKeys["customerId"]);
        Assert.Equal(1, snapshot.ConcurrencyVersion);
        Assert.True(snapshot.SystemData.ContainsKey("_auditTrail"));
    }

    [Fact]
    public void ContextSnapshot_Throws_On_Immutable_Field_Mutation()
    {
        ContextSchemaParser.TryParse(SampleDeclarativeSchemaJson, out var schema);

        var initialData = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["orderId"] = JsonSerializer.SerializeToElement("ORD-101"),
            ["totalAmount"] = JsonSerializer.SerializeToElement(100.0)
        };

        var snapshot = new WorkflowContextSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            initialData,
            schema: schema);

        // Attempt to change immutable orderId
        var invalidMutation = new[]
        {
            ContextOperation.Set("orderId", JsonSerializer.SerializeToElement("ORD-NEW-ILLEGAL"))
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            snapshot.ApplyOperations(invalidMutation, schema));

        Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContextSnapshot_Increments_Counter_Atomic_Primitive()
    {
        ContextSchemaParser.TryParse(SampleDeclarativeSchemaJson, out var schema);

        var initialData = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["orderId"] = JsonSerializer.SerializeToElement("ORD-102"),
            ["retryCount"] = JsonSerializer.SerializeToElement(0)
        };

        var snapshot = new WorkflowContextSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            initialData,
            schema: schema);

        // Increment retry count twice
        snapshot.ApplyOperations(new[] { ContextOperation.Increment("retryCount", 1) }, schema);
        snapshot.ApplyOperations(new[] { ContextOperation.Increment("retryCount", 2) }, schema);

        Assert.Equal(3, snapshot.CanonicalData["retryCount"].GetInt32());
        Assert.Equal(3, snapshot.ConcurrencyVersion);
    }

    [Fact]
    public void ContextSnapshot_Appends_To_AppendOnly_Array()
    {
        ContextSchemaParser.TryParse(SampleDeclarativeSchemaJson, out var schema);

        var initialData = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["orderId"] = JsonSerializer.SerializeToElement("ORD-103"),
            ["lineItems"] = JsonSerializer.SerializeToElement(new[] { "Item1" })
        };

        var snapshot = new WorkflowContextSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            initialData,
            schema: schema);

        snapshot.ApplyOperations(new[] { ContextOperation.Append("lineItems", JsonSerializer.SerializeToElement("Item2")) }, schema);

        var items = JsonSerializer.Deserialize<List<string>>(snapshot.CanonicalData["lineItems"].GetRawText());
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        Assert.Equal("Item1", items[0]);
        Assert.Equal("Item2", items[1]);
    }

    [Fact]
    public void ContextSnapshot_DeepMerge_Merges_Nested_Objects()
    {
        var initialData = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["metadata"] = JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["source"] = "Mobile",
                ["tier"] = "Gold"
            })
        };

        var snapshot = new WorkflowContextSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            initialData);

        snapshot.ApplyOperations(new[]
        {
            ContextOperation.DeepMerge("metadata", JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["tier"] = "Platinum",
                ["region"] = "US"
            }))
        });

        var merged = JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.CanonicalData["metadata"].GetRawText());
        Assert.NotNull(merged);
        Assert.Equal("Mobile", merged["source"]);
        Assert.Equal("Platinum", merged["tier"]);
        Assert.Equal("US", merged["region"]);
    }

    [Fact]
    public void PreparedWorkflowContext_Recomputes_Derived_Fields_On_Commit()
    {
        ContextSchemaParser.TryParse(SampleDeclarativeSchemaJson, out var schema);

        var initialData = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["orderId"] = JsonSerializer.SerializeToElement("ORD-200"),
            ["totalAmount"] = JsonSerializer.SerializeToElement(100.0)
        };

        var snapshot = new WorkflowContextSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            initialData,
            schema: schema);

        // Delta changes totalAmount to 8000 (which makes riskScore = 0.8)
        var delta = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["totalAmount"] = JsonSerializer.SerializeToElement(8000.0)
        };

        var prepared = new PreparedWorkflowContext(
            null!,
            snapshot,
            delta,
            new Dictionary<string, object>(),
            schema);

        prepared.CommitDelta();

        Assert.Equal(8000.0, snapshot.CanonicalData["totalAmount"].GetDouble());
        Assert.True(snapshot.CanonicalData.ContainsKey("riskScore"));
        Assert.Equal(0.8, snapshot.CanonicalData["riskScore"].GetDouble());
    }
}
