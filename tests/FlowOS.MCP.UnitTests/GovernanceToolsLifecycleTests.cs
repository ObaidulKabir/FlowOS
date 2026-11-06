using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Governance;
using FlowOS.Application.DTOs.Governance;
using FlowOS.Domain.Enums;
using FlowOS.MCP.Services;
using FlowOS.MCP.Tools;
using MediatR;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlowOS.MCP.UnitTests;

public sealed class GovernanceToolsLifecycleTests
{
    private readonly Mock<IMediator> _mediatorMock = new();

    // Tenant integration finding H1 (P0): the Application layer already has
    // CreateNewWorkflowClassVersionCommand / DeprecateWorkflowClassCommand / DeleteWorkflowClassCommand
    // handlers (exposed over REST), but no MCP tool ever called them, so an agent working only
    // through MCP had no way to version, deprecate, or delete a WorkflowClass.

    [Fact]
    public async Task NewVersion_Success_ReturnsNewDraftLinkedToPrevious()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var sourceId = Guid.NewGuid();
            var newId = Guid.NewGuid();

            _mediatorMock
                .Setup(m => m.Send(It.Is<CreateNewWorkflowClassVersionCommand>(c =>
                        c.TenantId == tenantId && c.Id == sourceId && c.BumpType == VersionBumpType.Major),
                    default))
                .ReturnsAsync(new WorkflowClassResponseDto
                {
                    Id = newId,
                    TenantId = tenantId,
                    Name = "Approval",
                    Version = "2.0.0",
                    PreviousVersionId = sourceId,
                    Status = WorkflowClassStatus.Draft
                });

            var tools = new GovernanceTools(_mediatorMock.Object);
            var result = await tools.NewVersion(JObject.FromObject(new
            {
                id = sourceId,
                tenantId,
                bumpType = "Major"
            }));

            Assert.False(result.IsError);
            var json = JObject.Parse(result.Content.Single().Text)["data"]!;
            Assert.Equal(newId, Guid.Parse(json["id"]!.ToString()));
            Assert.Equal("2.0.0", json["version"]!.ToString());
            Assert.Equal(sourceId, Guid.Parse(json["previousVersionId"]!.ToString()));
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task NewVersion_InvalidBumpType_FailsWithoutCallingMediator()
    {
        McpRequestContext.Clear();
        try
        {
            var tools = new GovernanceTools(_mediatorMock.Object);
            var result = await tools.NewVersion(JObject.FromObject(new
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid(),
                bumpType = "Enormous"
            }));

            Assert.True(result.IsError);
            Assert.Contains("MCP-ARG-001", result.Content.Single().Text);
            _mediatorMock.Verify(m => m.Send(It.IsAny<CreateNewWorkflowClassVersionCommand>(), default), Times.Never);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task Deprecate_WithoutConfirmation_IsRejectedBeforeCallingMediator()
    {
        McpRequestContext.Clear();
        try
        {
            var tools = new GovernanceTools(_mediatorMock.Object);
            var result = await tools.Deprecate(JObject.FromObject(new
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            }));

            Assert.True(result.IsError);
            Assert.Contains("MCP-APPROVAL-REQUIRED", result.Content.Single().Text);
            _mediatorMock.Verify(m => m.Send(It.IsAny<DeprecateWorkflowClassCommand>(), default), Times.Never);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task Deprecate_WithConfirmation_Succeeds()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var id = Guid.NewGuid();
            _mediatorMock
                .Setup(m => m.Send(It.Is<DeprecateWorkflowClassCommand>(c => c.TenantId == tenantId && c.Id == id), default))
                .ReturnsAsync(new WorkflowClassResponseDto { Id = id, TenantId = tenantId, Status = WorkflowClassStatus.Deprecated });

            var tools = new GovernanceTools(_mediatorMock.Object);
            var result = await tools.Deprecate(JObject.FromObject(new
            {
                id,
                tenantId,
                confirmHumanApproval = true
            }));

            Assert.False(result.IsError);
            var json = JObject.Parse(result.Content.Single().Text)["data"]!;
            Assert.Equal("Deprecated", json["status"]!.ToString());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task Delete_WithoutConfirmation_IsRejectedBeforeCallingMediator()
    {
        McpRequestContext.Clear();
        try
        {
            var tools = new GovernanceTools(_mediatorMock.Object);
            var result = await tools.Delete(JObject.FromObject(new
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid()
            }));

            Assert.True(result.IsError);
            Assert.Contains("MCP-APPROVAL-REQUIRED", result.Content.Single().Text);
            _mediatorMock.Verify(m => m.Send(It.IsAny<DeleteWorkflowClassCommand>(), default), Times.Never);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task Delete_WhenNotDraft_SurfacesGovDel001AsArgError()
    {
        McpRequestContext.Clear();
        try
        {
            _mediatorMock
                .Setup(m => m.Send(It.IsAny<DeleteWorkflowClassCommand>(), default))
                .ThrowsAsync(new InvalidOperationException(
                    "GOV-DEL-001: Only Drafts can be hard deleted. Use Abandon for published workflows."));

            var tools = new GovernanceTools(_mediatorMock.Object);
            var result = await tools.Delete(JObject.FromObject(new
            {
                id = Guid.NewGuid(),
                tenantId = Guid.NewGuid(),
                confirmHumanApproval = true
            }));

            Assert.True(result.IsError);
            var text = result.Content.Single().Text;
            Assert.Contains("GOV-DEL-001", text);
            Assert.DoesNotContain("MCP-INTERNAL", text);
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }

    [Fact]
    public async Task Delete_WithConfirmation_Succeeds()
    {
        McpRequestContext.Clear();
        try
        {
            var tenantId = Guid.NewGuid();
            var id = Guid.NewGuid();
            _mediatorMock
                .Setup(m => m.Send(It.Is<DeleteWorkflowClassCommand>(c => c.TenantId == tenantId && c.Id == id), default))
                .ReturnsAsync(Unit.Value);

            var tools = new GovernanceTools(_mediatorMock.Object);
            var result = await tools.Delete(JObject.FromObject(new
            {
                id,
                tenantId,
                confirmHumanApproval = true
            }));

            Assert.False(result.IsError);
            var json = JObject.Parse(result.Content.Single().Text)["data"]!;
            Assert.Equal("Deleted", json["status"]!.ToString());
        }
        finally
        {
            McpRequestContext.Clear();
        }
    }
}
