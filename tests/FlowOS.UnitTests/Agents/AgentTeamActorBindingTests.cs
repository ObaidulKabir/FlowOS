using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Application.Services;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Workflows.Enums;
using Xunit;

namespace FlowOS.UnitTests.Agents;

/// <summary>
/// ADR-003: a WorkflowClassBlueprint step's agent actor can bind to an orchestrated AgentTeam
/// (AgentTeamId/AgentTeamSlotRole) instead of a single inline agent (AgentProvider/AgentPrompt).
/// The team actor only takes effect when StepType is "AgentTask" -
/// AgentTaskScheduling.ForCurrentWaitingStep is the sole runtime reader of AgentTeamId and it
/// only consults that field for AgentTask steps - so these pin exactly that constraint rather
/// than inventing a broader one the dispatcher doesn't enforce.
/// </summary>
public class AgentTeamActorBindingTests
{
    private static StepBlueprint AgentTaskStep(Guid? teamId = null, string? slotRole = null, string? agentProvider = null) => new()
    {
        StepId = "Triage",
        StepType = "AgentTask",
        Actor = "Agent",
        DecisionGuideline = "Triage the ticket and propose the next event.",
        AgentTeamId = teamId,
        AgentTeamSlotRole = slotRole,
        AgentProvider = agentProvider,
        NextSteps = new Dictionary<string, string> { ["EVT-TRIAGED"] = "END" }
    };

    private static WorkflowClassBlueprint Blueprint(StepBlueprint step) => new()
    {
        Events = new List<EventBlueprint> { new() { EventId = "EVT-TRIAGED", Name = "Triaged" } },
        StateMachine = new StateMachineBlueprint
        {
            InitialState = "Open",
            States = new List<string> { "Open", "Triaged" },
            Transitions = new List<TransitionBlueprint>
            {
                new() { FromState = "Open", ToState = "Triaged", EventId = "EVT-TRIAGED" }
            }
        },
        Workflow = new WorkflowBlueprint { StartStepId = step.StepId, Steps = new List<StepBlueprint> { step } }
    };

    [Fact]
    public void ValidTeamActorStep_IsAccepted()
    {
        var blueprint = Blueprint(AgentTaskStep(teamId: Guid.NewGuid(), slotRole: "Triager"));

        var errors = new WorkflowClassValidator().Validate(blueprint).Errors;

        Assert.DoesNotContain(errors, e => e.Code.StartsWith("WF-AGENT-00"));
    }

    [Fact]
    public void SlotRoleWithoutTeamId_AddsAgent007()
    {
        var blueprint = Blueprint(AgentTaskStep(teamId: null, slotRole: "Triager"));

        var errors = new WorkflowClassValidator().Validate(blueprint).Errors;

        Assert.Contains(errors, e => e.Code == "WF-AGENT-007");
    }

    [Fact]
    public void TeamIdOnNonAgentTaskStepType_AddsAgent008()
    {
        var step = AgentTaskStep(teamId: Guid.NewGuid());
        step.StepType = "HumanTask";
        step.Actor = "Agent";

        var errors = new WorkflowClassValidator().Validate(Blueprint(step)).Errors;

        Assert.Contains(errors, e => e.Code == "WF-AGENT-008");
    }

    [Fact]
    public void TeamIdOnAgentTaskStep_DoesNotAddAgent008()
    {
        var blueprint = Blueprint(AgentTaskStep(teamId: Guid.NewGuid()));

        var errors = new WorkflowClassValidator().Validate(blueprint).Errors;

        Assert.DoesNotContain(errors, e => e.Code == "WF-AGENT-008");
    }

    [Fact]
    public void TeamIdAndAgentProviderBothSet_AddsAgent010()
    {
        var blueprint = Blueprint(AgentTaskStep(teamId: Guid.NewGuid(), agentProvider: "RiskAnalysisAgent"));

        var errors = new WorkflowClassValidator().Validate(blueprint).Errors;

        Assert.Contains(errors, e => e.Code == "WF-AGENT-010");
    }

    [Fact]
    public void AgentProviderAloneWithoutTeam_DoesNotAddAgent010()
    {
        var blueprint = Blueprint(AgentTaskStep(teamId: null, agentProvider: "RiskAnalysisAgent"));

        var errors = new WorkflowClassValidator().Validate(blueprint).Errors;

        Assert.DoesNotContain(errors, e => e.Code == "WF-AGENT-010");
    }

    [Fact]
    public void EmptyGuidTeamId_IsTreatedAsNoTeam()
    {
        var blueprint = Blueprint(AgentTaskStep(teamId: Guid.Empty, slotRole: "Triager"));

        var errors = new WorkflowClassValidator().Validate(blueprint).Errors;

        // Guid.Empty is not a real team reference, so this is the "slot role without a team" case.
        Assert.Contains(errors, e => e.Code == "WF-AGENT-007");
        Assert.DoesNotContain(errors, e => e.Code == "WF-AGENT-008");
    }

    [Fact]
    public void Compiler_CarriesTeamBindingToTheRuntimeStep()
    {
        var teamId = Guid.NewGuid();
        var step = AgentTaskStep(teamId: teamId, slotRole: "  Triager  ");
        var workflowClass = new WorkflowClass(Guid.NewGuid(), "Triage", "1.0.0", Blueprint(step));

        var definition = WorkflowClassCompiler.MapToRuntimeDefinition(workflowClass);
        var compiled = definition.Steps.Single(s => s.StepId == "Triage");

        Assert.Equal(WorkflowStepType.AgentTask, compiled.StepType);
        Assert.Equal(teamId, compiled.AgentTeamId);
        Assert.Equal("Triager", compiled.AgentTeamSlotRole);
    }

    [Fact]
    public void Compiler_LeavesTeamBindingNullWhenStepDeclaresNone()
    {
        var step = AgentTaskStep();
        var workflowClass = new WorkflowClass(Guid.NewGuid(), "Triage", "1.0.0", Blueprint(step));

        var definition = WorkflowClassCompiler.MapToRuntimeDefinition(workflowClass);
        var compiled = definition.Steps.Single(s => s.StepId == "Triage");

        Assert.Null(compiled.AgentTeamId);
        Assert.Null(compiled.AgentTeamSlotRole);
    }
}
