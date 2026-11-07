using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Governance;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Application.Queries.Governance;
using FlowOS.Domain.Blueprints;
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

    /// <summary>
    /// DX report F8: get_draft_workflowclass returned the blueprint only under "Definition"
    /// (PascalCase), while update_draft_workflowclass requires it under the top-level key
    /// "blueprint" - a passed-through read response couldn't be fed straight back into the setter.
    /// This asserts the response now also carries the identical object under "blueprint".
    /// </summary>
    [Fact]
    public async Task GetDraft_ResponseIncludesBlueprintAlias_ReadyForUpdateDraft()
    {
        McpRequestContext.Clear();
        try
        {
            var classId = Guid.NewGuid();
            var tenantId = Guid.NewGuid();
            var dto = new WorkflowClassResponseDto
            {
                Id = classId,
                TenantId = tenantId,
                Name = "Approval",
                Version = "1.0.0",
                Definition = new WorkflowClassBlueprint()
            };
            _mediatorMock
                .Setup(m => m.Send(It.IsAny<GetWorkflowClassByIdQuery>(), default))
                .ReturnsAsync(dto);

            var tools = new GovernanceTools(_mediatorMock.Object);

            var result = await tools.GetDraft(JObject.FromObject(new
            {
                id = classId,
                tenantId
            }));

            Assert.False(result.IsError);
            var data = JObject.Parse(result.Content.Single().Text)["data"];
            Assert.NotNull(data!["blueprint"]);
            Assert.NotNull(data["Definition"]);
            Assert.Equal(data["Definition"]!.ToString(), data["blueprint"]!.ToString());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    /// <summary>
    /// DX report F8: the "blueprint is required" error on create/update now lists the argument
    /// names actually received, so a missing or misnamed blueprint is diagnosable from the error
    /// alone instead of needing a follow-up round-trip.
    /// </summary>
    [Fact]
    public async Task CreateDraft_MissingBlueprint_ListsReceivedArgs()
    {
        McpRequestContext.Clear();
        try
        {
            var tools = new GovernanceTools(_mediatorMock.Object);

            var result = await tools.CreateDraft(JObject.FromObject(new
            {
                name = "Approval",
                tenantId = Guid.NewGuid()
            }));

            Assert.True(result.IsError);
            var text = result.Content.Single().Text;
            Assert.Contains("blueprint is required", text);
            Assert.Contains("name", text);
            Assert.Contains("tenantId", text);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }
}
