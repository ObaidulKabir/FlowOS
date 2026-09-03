using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

public sealed class ContractAndTenantTests
{
    [Fact]
    public void Every_tool_has_self_describing_behavior_and_valid_example()
    {
        Assert.Equal(21, McpToolDescriptions.All.Count);

        Assert.All(McpToolDescriptions.All, contract =>
        {
            Assert.Contains("Returns:", contract.Value);
            Assert.Contains("Errors:", contract.Value);
            Assert.Contains("Input example:", contract.Value);

            var example = contract.Value[(contract.Value.IndexOf("Input example:", StringComparison.Ordinal)
                + "Input example:".Length)..].Trim();
            Assert.IsType<JObject>(JToken.Parse(example));
        });
    }

    [Fact]
    public void All_contract_factories_are_object_schemas()
    {
        var schemas = new[]
        {
            McpToolSchemas.NoArguments(),
            McpToolSchemas.TenantOptional(),
            McpToolSchemas.ListNotifications(),
            McpToolSchemas.MarkNotificationAsRead(),
            McpToolSchemas.SuggestAgentAction(),
            McpToolSchemas.ExplainValidationViolation(),
            McpToolSchemas.DraftById(),
            McpToolSchemas.CreateDraft(),
            McpToolSchemas.UpdateDraft(),
            McpToolSchemas.WorkflowInstanceStatus(),
            McpToolSchemas.DraftById("publicId"),
            McpToolSchemas.BlueprintSchema(),
            McpToolSchemas.StartWorkflow(),
            McpToolSchemas.PublishEvent(),
            McpToolSchemas.CompleteTask(),
            McpToolSchemas.ListWorkflowInstances()
        };

        Assert.All(schemas, schema =>
        {
            Assert.Equal("object", schema["type"]?.ToString());
            Assert.NotNull(schema["properties"]);
            Assert.Equal(false, schema["additionalProperties"]?.Value<bool>());
        });

        var categories = McpToolSchemas.BlueprintSchema()
            ["properties"]?["events"]?["items"]?["properties"]?["category"]?["enum"]!
            .Values<string>();
        Assert.Equal(new[] { "Decision", "System", "Human", "Agent" }, categories);
    }

    [Fact]
    public void Tenant_resolver_requires_explicit_stdio_tenant()
    {
        McpRequestContext.Clear();
        var error = Assert.Throws<McpToolException>(() =>
            McpTenantResolver.ResolveRequired(new JObject()));
        Assert.Equal("MCP-TENANT-001", error.Code);
    }

    [Fact]
    public void Authenticated_tenant_cannot_be_overridden()
    {
        McpRequestContext.Clear();
        McpRequestContext.IsAuthenticatedTransport = true;
        McpRequestContext.TenantId = Guid.NewGuid();
        try
        {
            var error = Assert.Throws<McpToolException>(() =>
                McpTenantResolver.ResolveRequired(JObject.FromObject(new { tenantId = Guid.NewGuid() })));
            Assert.Equal("MCP-TENANT-002", error.Code);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task Registry_sanitizes_unhandled_exceptions()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.Register("explode", "test", McpToolSchemas.NoArguments(),
            _ => throw new InvalidOperationException("database-password"));

        var result = await registry.ExecuteAsync("explode", new JObject());

        Assert.True(result.IsError);
        Assert.DoesNotContain("database-password", result.Content.Single().Text);
        Assert.Contains("MCP-INTERNAL", result.Content.Single().Text);
    }
}
