using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Governance;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using FlowOS.Workflows.Domain;
using MediatR;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.MCP.UnitTests;

public sealed class GovernanceToolsTests
{
    private readonly Mock<IMediator> _mediatorMock = new();

    [Fact]
    public async Task Publish_WhenDefinitionPinnedToDifferentClass_ReturnsWfNamePinnedNotMcpInternal()
    {
        // Tenant integration finding H2: this used to fall through to the generic
        // `catch (Exception ex) => MCP-INTERNAL` branch, hiding a well-known, actionable conflict
        // (a runtime definition with this (Name, Version) is already pinned to another
        // WorkflowClass) behind an opaque internal-error code.
        McpRequestContext.Clear();
        try
        {
            _mediatorMock
                .Setup(m => m.Send(It.IsAny<PublishWorkflowClassCommand>(), default))
                .ThrowsAsync(new WorkflowDefinitionPinConflictException(
                    "WF-NAME-PINNED: WorkflowDefinition 'Approval' v1 is already pinned to WorkflowClass 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' and cannot be re-pinned."));

            var tools = new GovernanceTools(_mediatorMock.Object);

            var result = await tools.Publish(JObject.FromObject(new
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid(),
                confirmHumanApproval = true
            }));

            Assert.True(result.IsError);
            var text = result.Content.Single().Text;
            Assert.Contains("WF-NAME-PINNED", text);
            Assert.DoesNotContain("MCP-INTERNAL", text);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }
}
