using FlowOS.Application.Commands.Orchestration;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Services;
using FlowOS.MCP.Tools;
using MediatR;
using Moq;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.UnitTests;

/// <summary>
/// ADR-007 §8: the MCP surface for AgentTeamBlueprint validation. The mediator forwards to
/// the real <see cref="AgentTeamBlueprintValidator"/> so these pin the JSON shape, not the
/// validation rules themselves (those are covered in FlowOS.UnitTests).
/// </summary>
public class AgentTeamBlueprintMcpToolsTests
{
    private static AgentTeamBlueprintMcpTools BuildTool()
    {
        var validator = new AgentTeamBlueprintValidator();
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<ValidateAgentTeamBlueprintCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ValidateAgentTeamBlueprintCommand cmd, CancellationToken _) => validator.Validate(cmd.Definition));

        return new AgentTeamBlueprintMcpTools(mediator.Object);
    }

    [Fact]
    public async Task Validate_ValidRoster_ReturnsIsValidTrue()
    {
        var tool = BuildTool();
        var result = await tool.Validate(JObject.FromObject(new
        {
            definition = new
            {
                name = "RiskAssessmentTeam",
                objective = "Assess quarterly portfolio risk",
                slots = new[]
                {
                    new { roleName = "RiskAnalyst", requiredCapabilities = new[] { "ticket-system.search" } },
                },
            },
        }));

        var payload = JObject.Parse(result.Content.Single().Text!);
        Assert.False(result.IsError);
        Assert.True(payload["data"]!["isValid"]!.Value<bool>());
        Assert.Equal(0, payload["data"]!["errorCount"]!.Value<int>());
    }

    [Fact]
    public async Task Validate_NoSlots_ReturnsIsValidFalseWithAtmStr004()
    {
        var tool = BuildTool();
        var result = await tool.Validate(JObject.FromObject(new
        {
            definition = new
            {
                name = "RiskAssessmentTeam",
                objective = "Assess quarterly portfolio risk",
                slots = Array.Empty<object>(),
            },
        }));

        var payload = JObject.Parse(result.Content.Single().Text!);
        Assert.False(result.IsError); // an invalid blueprint is still a successful call
        Assert.False(payload["data"]!["isValid"]!.Value<bool>());
        var codes = ((JArray)payload["data"]!["errors"]!).Select(e => e["code"]!.ToString());
        Assert.Contains("ATM-STR-004", codes);
    }

    [Fact]
    public async Task Validate_MissingDefinition_ReturnsArgError()
    {
        var tool = BuildTool();
        var result = await tool.Validate(new JObject());

        var payload = JObject.Parse(result.Content.Single().Text!);
        Assert.True(result.IsError);
        Assert.Equal("MCP-ARG-001", payload["errorCode"]!.Value<string>());
    }
}
