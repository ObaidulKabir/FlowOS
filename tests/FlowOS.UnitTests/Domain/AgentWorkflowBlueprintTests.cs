using System;
using System.Collections.Generic;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;
using Xunit;

namespace FlowOS.UnitTests.Domain;

/// <summary>
/// ADR-006 Phase 4A: AgentWorkflowBlueprint record tree construction tests.
/// Records are immutable by design; these tests document expected default values
/// and structural invariants that the blueprint authoring layer will rely on.
/// </summary>
public class AgentWorkflowBlueprintTests
{
    // ── AgentWorkflowBlueprint ─────────────────────────────────────────────────

    [Fact]
    public void Blueprint_DefaultConstruct_HasSensibleDefaults()
    {
        var bp = new AgentWorkflowBlueprint();
        Assert.Equal(string.Empty, bp.Objective);
        Assert.Equal(string.Empty, bp.StartStepId);
        Assert.Empty(bp.Steps);
        Assert.NotNull(bp.CoordinationRules);
        Assert.Null(bp.ContextSchema);
    }

    [Fact]
    public void Blueprint_FullyConstructed_RoundTrips_Via_Init()
    {
        var bp = new AgentWorkflowBlueprint
        {
            Objective = "Monthly compliance review",
            StartStepId = "gather",
            ContextSchema = """{"type":"object","properties":{"tenantCode":{"type":"string"}}}""",
            CoordinationRules = new AgentCoordinationRulesBlueprint
            {
                MaxGlobalRetries = 5,
                RollbackStrategy = "Compensate",
                GlobalSla = "PT4H",
                MaxParallelSteps = 3
            },
            Steps =
            [
                new AgentStepBlueprint { StepId = "gather", Description = "Gather data" },
                new AgentStepBlueprint { StepId = "review", Description = "Human review" }
            ]
        };

        Assert.Equal("Monthly compliance review", bp.Objective);
        Assert.Equal("gather", bp.StartStepId);
        Assert.Equal(2, bp.Steps.Count);
        Assert.Equal(5, bp.CoordinationRules.MaxGlobalRetries);
        Assert.Equal("Compensate", bp.CoordinationRules.RollbackStrategy);
        Assert.Equal("PT4H", bp.CoordinationRules.GlobalSla);
        Assert.Equal(3, bp.CoordinationRules.MaxParallelSteps);
    }

    // ── AgentCoordinationRulesBlueprint ────────────────────────────────────────

    [Fact]
    public void CoordinationRules_Defaults()
    {
        var rules = new AgentCoordinationRulesBlueprint();
        Assert.Equal(3, rules.MaxGlobalRetries);
        Assert.Equal("Abandon", rules.RollbackStrategy);
        Assert.Null(rules.GlobalSla);
        Assert.Equal(0, rules.MaxParallelSteps);  // 0 = unlimited
    }

    // ── AgentStepBlueprint ─────────────────────────────────────────────────────

    [Fact]
    public void Step_OmittedStepType_DeserialisesAsAgent()
    {
        // The blueprint lives in one jsonb column, so a document written before StepType
        // existed must still load. The record default carries it.
        var json = """{"stepId":"legacy","description":"written before StepType existed"}""";

        var step = System.Text.Json.JsonSerializer.Deserialize<AgentStepBlueprint>(
            json,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

        Assert.NotNull(step);
        Assert.Equal("legacy", step!.StepId);
        Assert.Equal(AgentStepType.Agent, step.StepType);
    }

    [Fact]
    public void Step_DefaultConstruct_HasSensibleDefaults()
    {
        var step = new AgentStepBlueprint();
        Assert.Equal(string.Empty, step.StepId);
        Assert.Equal(AgentStepType.Agent, step.StepType);
        Assert.Equal(string.Empty, step.Description);
        Assert.Null(step.AgentTeamId);
        Assert.Null(step.TeamRole);
        Assert.Empty(step.NextSteps);
        Assert.Empty(step.Branches);
        Assert.Equal("WaitAll", step.JoinPolicy);
        Assert.Empty(step.InboundSteps);
        Assert.Null(step.MaxAutonomyLevel);
        Assert.False(step.Consequential);
        Assert.Null(step.AutoCommit);
        Assert.Null(step.Sla);
        Assert.Null(step.AgentPrompt);
        Assert.Empty(step.AgentTools);
        Assert.Empty(step.PathLimits);
    }

    [Fact]
    public void Step_WithNextSteps_BuildsRoutingTable()
    {
        var step = new AgentStepBlueprint
        {
            StepId = "assess",
            NextSteps =
            {
                { "Approved", "notify" },
                { "Rejected", "escalate" }
            }
        };

        Assert.Equal(2, step.NextSteps.Count);
        Assert.Equal("notify", step.NextSteps["Approved"]);
        Assert.Equal("escalate", step.NextSteps["Rejected"]);
    }

    [Fact]
    public void Step_WithForkJoin_BuildsBranchesAndInbounds()
    {
        var fork = new AgentStepBlueprint
        {
            StepId = "fork",
            Branches = ["branch-a", "branch-b"]
        };

        var join = new AgentStepBlueprint
        {
            StepId = "join",
            JoinPolicy = "WaitAll",
            InboundSteps = ["branch-a", "branch-b"]
        };

        Assert.Equal(2, fork.Branches.Count);
        Assert.Equal(2, join.InboundSteps.Count);
        Assert.Equal("WaitAll", join.JoinPolicy);
    }

    [Fact]
    public void Step_ConsequentialFlag_BlocksAutoCommit()
    {
        var step = new AgentStepBlueprint
        {
            StepId = "pay",
            Consequential = true,
            // AutoCommit is intentionally null for consequential steps
            AutoCommit = null
        };

        Assert.True(step.Consequential);
        Assert.Null(step.AutoCommit);
    }

    [Fact]
    public void Step_WithSla_SetsDuration()
    {
        var step = new AgentStepBlueprint
        {
            StepId = "review",
            Sla = new AgentStepSlaBlueprint { Duration = "PT2H", EscalationRole = "RiskManager" }
        };

        Assert.Equal("PT2H", step.Sla!.Duration);
        Assert.Equal("RiskManager", step.Sla.EscalationRole);
    }

    [Fact]
    public void Step_AutoCommit_SetsMinConfidence()
    {
        var step = new AgentStepBlueprint
        {
            StepId = "data-pull",
            AutoCommit = new AgentStepAutoCommitBlueprint { MinConfidence = 0.95 }
        };

        Assert.Equal(0.95, step.AutoCommit!.MinConfidence);
    }

    [Fact]
    public void Step_PathLimits_TrackRetryAndResubmit()
    {
        var step = new AgentStepBlueprint
        {
            StepId = "process",
            PathLimits =
            {
                ["Retry"] = new AgentPathTravelLimitBlueprint { MaxTravels = 3 },
                ["Resubmit"] = new AgentPathTravelLimitBlueprint { MaxTravels = 1 }
            }
        };

        Assert.Equal(3, step.PathLimits["Retry"].MaxTravels);
        Assert.Equal(1, step.PathLimits["Resubmit"].MaxTravels);
    }

    // ── Blueprint structural equality (record semantics) ───────────────────────

    [Fact]
    public void Blueprint_Records_SupportValueEquality()
    {
        var rules1 = new AgentCoordinationRulesBlueprint { MaxGlobalRetries = 2, RollbackStrategy = "Abandon" };
        var rules2 = new AgentCoordinationRulesBlueprint { MaxGlobalRetries = 2, RollbackStrategy = "Abandon" };
        Assert.Equal(rules1, rules2);
    }
}
