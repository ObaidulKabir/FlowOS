using System;
using System.Linq;
using System.Threading.Tasks;
using FlowOS.Application.Commands.Orchestration;
using FlowOS.Domain.Blueprints;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using MediatR;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

/// <summary>
/// ADR-007 §8: validation for the declarative AgentTeamBlueprint roster, exposed over MCP
/// so a coding agent can design a team's shape without a dashboard.
///
/// Validate-only today — there is no AgentTeamClass to create, update or publish yet.
/// Persistence, versioning and compilation to AgentTeam follow in a later PR, once the
/// ADR's own open lifecycle question (AgentTeamStatus vs. blueprint lifecycle) is settled.
/// </summary>
public class AgentTeamBlueprintMcpTools
{
    private readonly IMediator _mediator;

    public AgentTeamBlueprintMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Check a team roster without storing it. An invalid roster is a successful call
    /// reporting every problem at once, not a failure.
    /// </summary>
    public async Task<CallToolResult> Validate(JObject args)
    {
        try
        {
            var definitionJson = args["definition"] as JObject;
            if (definitionJson == null) return McpToolResults.Fail("MCP-ARG-001", "definition is required.");

            var definition = definitionJson.ToObject<AgentTeamBlueprint>();
            if (definition == null) return McpToolResults.Fail("MCP-ARG-001", "definition format is invalid.");

            var validation = await _mediator.Send(new ValidateAgentTeamBlueprintCommand(definition));

            return McpToolResults.Success(new
            {
                isValid = validation.IsValid,
                errorCount = validation.Errors.Count,
                errors = validation.Errors.Select(e => new
                {
                    code = e.Code,
                    category = e.Category,
                    message = e.Message,
                    element = e.Element,
                }),
            });
        }
        catch (McpToolException ex) { return McpToolResults.Fail(ex.Code, ex.Message); }
        catch (Exception ex)
        {
            return McpToolResults.Fail("MCP-INTERNAL", $"Failed to validate agent team blueprint: {ex.Message}");
        }
    }
}
