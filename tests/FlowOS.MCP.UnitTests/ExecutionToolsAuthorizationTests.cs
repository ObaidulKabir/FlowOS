using FlowOS.Application.Commands;
using FlowOS.Application.Common.Exceptions;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using MediatR;
using Moq;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

public class ExecutionToolsAuthorizationTests
{
    [Fact]
    public async Task StartWorkflow_PolicyViolation_ReturnsStableAuthorizationError()
    {
        var tenantId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(item => item.Send(It.IsAny<StartWorkflowCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PolicyViolationException(
                "CapabilityCheck",
                "Missing required capability: workflow.start"));
        var tool = new ExecutionTools(mediator.Object);

        McpRequestContext.TenantId = tenantId;
        try
        {
            var result = await tool.StartWorkflow(JObject.FromObject(new
            {
                tenantId,
                workflowName = "SalesFlow"
            }));
            var payload = JObject.Parse(result.Content.Single().Text!);

            Assert.True(result.IsError);
            Assert.Equal("MCP-AUTHZ-001", payload["errorCode"]!.Value<string>());
            Assert.Equal("CapabilityCheck", payload["context"]!["policyName"]!.Value<string>());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }
}
