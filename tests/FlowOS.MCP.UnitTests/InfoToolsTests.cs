using System.Linq;
using System.Threading.Tasks;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using MediatR;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.MCP.UnitTests;

public sealed class InfoToolsTests
{
    private readonly Mock<IMediator> _mediatorMock = new();

    /// <summary>
    /// DX report F7: get_workflow_instance_status was the only instance-scoped tool that did not
    /// accept workflowInstanceId - the canonical name start_workflow's response and publish_event
    /// use - forcing a round-trip to discover the mismatch. This asserts workflowInstanceId is now
    /// accepted (and preferred over the deprecated instanceId/id synonyms when more than one is sent).
    /// </summary>
    [Fact]
    public async Task GetWorkflowInstanceStatus_AcceptsWorkflowInstanceId()
    {
        McpRequestContext.Clear();
        try
        {
            var tools = new InfoTools(_mediatorMock.Object);

            var result = await tools.GetWorkflowInstanceStatus(JObject.FromObject(new
            {
                workflowInstanceId = "not-a-guid",
                tenantId = System.Guid.NewGuid()
            }));

            Assert.True(result.IsError);
            var text = result.Content.Single().Text;
            Assert.Contains("workflowInstanceId must be a valid UUID", text);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task GetWorkflowInstanceStatus_MissingAllSynonyms_ReportsReceivedArgs()
    {
        McpRequestContext.Clear();
        try
        {
            var tools = new InfoTools(_mediatorMock.Object);

            var result = await tools.GetWorkflowInstanceStatus(JObject.FromObject(new
            {
                tenantId = System.Guid.NewGuid()
            }));

            Assert.True(result.IsError);
            var text = result.Content.Single().Text;
            Assert.Contains("workflowInstanceId must be a valid UUID", text);
            Assert.Contains("tenantId", text);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }
}
