using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Enums;
using FlowOS.Domain.Services;
using FlowOS.Domain.Validation;
using Xunit;

namespace FlowOS.UnitTests.Domain;

/// <summary>
/// ADR-006: graph-integrity validation of AgentWorkflowBlueprint.
///
/// A published AgentWorkflowClass is immutable and dispatchable, so a blueprint that
/// references a step that does not exist, or declares a fork with one branch, would be
/// permanently broken. These tests pin each guard to its error code.
/// </summary>
public class AgentWorkflowBlueprintValidatorTests
{
    private static readonly AgentWorkflowBlueprintValidator Validator = new();

    // ── Builders ──────────────────────────────────────────────────────────────

    /// <summary>A two-step linear plan that must validate cleanly.</summary>
    private static AgentWorkflowBlueprint ValidPlan() => new()
    {
        Objective = "Assess quarterly portfolio risk",
        StartStepId = "gather",
        Steps = new List<AgentStepBlueprint>
        {
            new()
            {
                StepId = "gather",
                TeamRole = "DataAnalyst",
                NextSteps = new Dictionary<string, string> { ["Gathered"] = "assess" },
            },
            new()
            {
                StepId = "assess",
                TeamRole = "RiskAnalyst",
                NextSteps = new Dictionary<string, string> { ["Assessed"] = "END" },
            },
        },
    };

    /// <summary>A fork/join plan that must validate cleanly.</summary>
    private static AgentWorkflowBlueprint ValidForkJoinPlan() => new()
    {
        Objective = "Parallel due diligence",
        StartStepId = "split",
        Steps = new List<AgentStepBlueprint>
        {
            new()
            {
                StepId = "split",
                StepType = AgentStepType.Fork,
                Branches = new List<string> { "legal", "financial" },
            },
            new()
            {
                StepId = "legal",
                TeamRole = "LegalReview",
                NextSteps = new Dictionary<string, string> { ["Reviewed"] = "merge" },
            },
            new()
            {
                StepId = "financial",
                TeamRole = "FinanceReview",
                NextSteps = new Dictionary<string, string> { ["Reviewed"] = "merge" },
            },
            new()
            {
                StepId = "merge",
                StepType = AgentStepType.Join,
                JoinPolicy = "WaitAll",
                InboundSteps = new List<string> { "legal", "financial" },
                NextSteps = new Dictionary<string, string> { ["Merged"] = "done" },
            },
            new()
            {
                StepId = "done",
                StepType = AgentStepType.End,
            },
        },
    };

    private static AgentWorkflowBlueprint PlanWith(params AgentStepBlueprint[] steps) => new()
    {
        Objective = "Test plan",
        StartStepId = steps.Length > 0 ? steps[0].StepId : string.Empty,
        Steps = steps.ToList(),
    };

    private static void AssertHasError(ValidationResult result, string code) =>
        Assert.Contains(code, result.Errors.Select(e => e.Code));

    private static void AssertNoError(ValidationResult result, string code) =>
        Assert.DoesNotContain(code, result.Errors.Select(e => e.Code));

    private static string Describe(ValidationResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}"));

    // ── Happy paths ───────────────────────────────────────────────────────────

    [Fact]
    public void ValidLinearPlan_IsValid()
    {
        var result = Validator.Validate(ValidPlan());
        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void ValidForkJoinPlan_IsValid()
    {
        var result = Validator.Validate(ValidForkJoinPlan());
        Assert.True(result.IsValid, Describe(result));
    }

    // ── Structure ─────────────────────────────────────────────────────────────

    [Fact]
    public void NullBlueprint_AddsStr000()
    {
        var result = Validator.Validate(null);
        AssertHasError(result, "AWF-STR-000");
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankObjective_AddsStr001(string objective)
    {
        var plan = ValidPlan() with { Objective = objective };
        AssertHasError(Validator.Validate(plan), "AWF-STR-001");
    }

    [Fact]
    public void NoSteps_AddsStr002()
    {
        var plan = ValidPlan() with { Steps = new List<AgentStepBlueprint>() };
        AssertHasError(Validator.Validate(plan), "AWF-STR-002");
    }

    [Fact]
    public void BlankStepId_AddsStr003()
    {
        var plan = PlanWith(
            new AgentStepBlueprint
            {
                StepId = "start",
                TeamRole = "A",
                NextSteps = new Dictionary<string, string> { ["Done"] = "END" },
            },
            new AgentStepBlueprint { StepId = "   ", TeamRole = "B" });

        AssertHasError(Validator.Validate(plan), "AWF-STR-003");
    }

    [Fact]
    public void DuplicateStepId_AddsStr004()
    {
        var plan = PlanWith(
            new AgentStepBlueprint
            {
                StepId = "twin",
                TeamRole = "A",
                NextSteps = new Dictionary<string, string> { ["Done"] = "END" },
            },
            new AgentStepBlueprint
            {
                StepId = "Twin",   // differs only by case
                TeamRole = "B",
                NextSteps = new Dictionary<string, string> { ["Done"] = "END" },
            });

        AssertHasError(Validator.Validate(plan), "AWF-STR-004");
    }

    [Fact]
    public void UnknownStepType_AddsStr005()
    {
        var plan = PlanWith(new AgentStepBlueprint
        {
            StepId = "odd",
            StepType = "Decision",   // belongs to WorkflowClass, not an agent plan
            TeamRole = "A",
            NextSteps = new Dictionary<string, string> { ["Done"] = "END" },
        });

        AssertHasError(Validator.Validate(plan), "AWF-STR-005");
    }

    // ── Entry point and reachability ──────────────────────────────────────────

    [Fact]
    public void MissingStartStepId_AddsComp001()
    {
        var plan = ValidPlan() with { StartStepId = "" };
        AssertHasError(Validator.Validate(plan), "AWF-COMP-001");
    }

    [Fact]
    public void DanglingStartStepId_AddsComp002()
    {
        var plan = ValidPlan() with { StartStepId = "nowhere" };
        AssertHasError(Validator.Validate(plan), "AWF-COMP-002");
    }

    [Fact]
    public void UnreachableStep_AddsComp003()
    {
        var plan = PlanWith(
            new AgentStepBlueprint
            {
                StepId = "start",
                TeamRole = "A",
                NextSteps = new Dictionary<string, string> { ["Done"] = "END" },
            },
            new AgentStepBlueprint
            {
                StepId = "orphan",
                TeamRole = "B",
                NextSteps = new Dictionary<string, string> { ["Done"] = "END" },
            });

        AssertHasError(Validator.Validate(plan), "AWF-COMP-003");
    }

    [Fact]
    public void SlaEscalationTarget_CountsAsReachable()
    {
        // A step reached only when an SLA breaches is legitimately reachable.
        var plan = PlanWith(
            new AgentStepBlueprint
            {
                StepId = "review",
                TeamRole = "Reviewer",
                NextSteps = new Dictionary<string, string> { ["Approved"] = "END" },
                Sla = new AgentStepSlaBlueprint
                {
                    Duration = "PT4H",
                    TimeoutEvent = "ReviewOverdue",
                    EscalationStepId = "escalate",
                },
            },
            new AgentStepBlueprint
            {
                StepId = "escalate",
                TeamRole = "Manager",
                NextSteps = new Dictionary<string, string> { ["Handled"] = "END" },
            });

        AssertNoError(Validator.Validate(plan), "AWF-COMP-003");
    }

    [Fact]
    public void PathLimitOverflowTarget_CountsAsReachable()
    {
        var plan = PlanWith(
            new AgentStepBlueprint
            {
                StepId = "attempt",
                TeamRole = "Worker",
                NextSteps = new Dictionary<string, string>
                {
                    ["Retry"] = "attempt",
                    ["Done"] = "END",
                },
                PathLimits = new Dictionary<string, AgentPathTravelLimitBlueprint>
                {
                    ["Retry"] = new() { MaxTravels = 3, OnExceeded = "giveUp" },
                },
            },
            new AgentStepBlueprint
            {
                StepId = "giveUp",
                TeamRole = "Manager",
                NextSteps = new Dictionary<string, string> { ["Closed"] = "END" },
            });

        AssertNoError(Validator.Validate(plan), "AWF-COMP-003");
    }

    [Fact]
    public void StepWithNoExit_AddsComp004()
    {
        var plan = PlanWith(new AgentStepBlueprint { StepId = "dead", TeamRole = "A" });
        AssertHasError(Validator.Validate(plan), "AWF-COMP-004");
    }

    // ── Step shape ────────────────────────────────────────────────────────────

    [Fact]
    public void EndStepWithNextSteps_AddsEnd001()
    {
        var plan = PlanWith(new AgentStepBlueprint
        {
            StepId = "finish",
            StepType = AgentStepType.End,
            NextSteps = new Dictionary<string, string> { ["Again"] = "END" },
        });

        AssertHasError(Validator.Validate(plan), "AWF-END-001");
    }

    [Fact]
    public void EndStepWithoutNextSteps_NeedsNoExitPathOrTeam()
    {
        var plan = PlanWith(new AgentStepBlueprint
        {
            StepId = "finish",
            StepType = AgentStepType.End,
        });

        var result = Validator.Validate(plan);
        AssertNoError(result, "AWF-COMP-004");
        AssertNoError(result, "AWF-TEAM-001");
    }

    [Fact]
    public void ForkWithOneBranch_AddsFork001()
    {
        var plan = PlanWith(
            new AgentStepBlueprint
            {
                StepId = "split",
                StepType = AgentStepType.Fork,
                Branches = new List<string> { "only" },
            },
            new AgentStepBlueprint
            {
                StepId = "only",
                TeamRole = "A",
                NextSteps = new Dictionary<string, string> { ["Done"] = "END" },
            });

        AssertHasError(Validator.Validate(plan), "AWF-FORK-001");
    }

    [Fact]
    public void ForkWithNoBranches_AddsComp004()
    {
        var plan = PlanWith(new AgentStepBlueprint
        {
            StepId = "split",
            StepType = AgentStepType.Fork,
        });

        AssertHasError(Validator.Validate(plan), "AWF-COMP-004");
    }

    [Fact]
    public void ForkThatAlsoJoins_AddsStep001()
    {
        var plan = ValidForkJoinPlan();
        var steps = plan.Steps.ToList();
        steps[0] = steps[0] with { InboundSteps = new List<string> { "legal", "financial" } };

        AssertHasError(Validator.Validate(plan with { Steps = steps }), "AWF-STEP-001");
    }

    [Fact]
    public void AgentStepWithBranches_AddsStep002()
    {
        // branches is fork-only; an Agent step carrying them is ambiguous.
        var plan = ValidPlan();
        var steps = plan.Steps.ToList();
        steps[0] = steps[0] with { Branches = new List<string> { "assess" } };

        AssertHasError(Validator.Validate(plan with { Steps = steps }), "AWF-STEP-002");
    }

    [Fact]
    public void AgentStepWithInboundSteps_AddsStep003()
    {
        var plan = ValidPlan();
        var steps = plan.Steps.ToList();
        steps[1] = steps[1] with { InboundSteps = new List<string> { "gather" } };

        AssertHasError(Validator.Validate(plan with { Steps = steps }), "AWF-STEP-003");
    }

    [Fact]
    public void JoinWithOneInbound_AddsJoin001()
    {
        var plan = ValidForkJoinPlan();
        var steps = plan.Steps.ToList();
        var join = steps.Single(s => s.StepId == "merge");
        steps[steps.IndexOf(join)] = join with { InboundSteps = new List<string> { "legal" } };

        AssertHasError(Validator.Validate(plan with { Steps = steps }), "AWF-JOIN-001");
    }

    [Fact]
    public void JoinWithUnknownPolicy_AddsJoin002()
    {
        var plan = ValidForkJoinPlan();
        var steps = plan.Steps.ToList();
        var join = steps.Single(s => s.StepId == "merge");
        steps[steps.IndexOf(join)] = join with { JoinPolicy = "majority" };

        AssertHasError(Validator.Validate(plan with { Steps = steps }), "AWF-JOIN-002");
    }

    [Theory]
    [InlineData("WaitAll")]
    [InlineData("WaitAny")]
    public void KnownJoinPolicies_AreAccepted(string policy)
    {
        var plan = ValidForkJoinPlan();
        var steps = plan.Steps.ToList();
        var join = steps.Single(s => s.StepId == "merge");
        steps[steps.IndexOf(join)] = join with { JoinPolicy = policy };

        AssertNoError(Validator.Validate(plan with { Steps = steps }), "AWF-JOIN-002");
    }

    [Fact]
    public void JoinWithoutExit_AddsComp004()
    {
        var plan = ValidForkJoinPlan();
        var steps = plan.Steps.ToList();
        var join = steps.Single(s => s.StepId == "merge");
        steps[steps.IndexOf(join)] = join with { NextSteps = new Dictionary<string, string>() };

        AssertHasError(Validator.Validate(plan with { Steps = steps }), "AWF-COMP-004");
    }

    // ── Routing consistency ──────────────────────────────────────────────────

    [Fact]
    public void NextStepsUnknownTarget_AddsCon001()
    {
        var plan = ValidPlan();
        var steps = plan.Steps.ToList();
        steps[0] = steps[0] with
        {
            NextSteps = new Dictionary<string, string> { ["Gathered"] = "ghost" },
        };

        AssertHasError(Validator.Validate(plan with { Steps = steps }), "AWF-CON-001");
    }

    [Fact]
    public void TerminalEndTarget_IsAlwaysKnown()
    {
        var plan = PlanWith(new AgentStepBlueprint
        {
            StepId = "only",
            TeamRole = "A",
            NextSteps = new Dictionary<string, string> { ["Done"] = "END" },
        });

        AssertNoError(Validator.Validate(plan), "AWF-CON-001");
    }

    [Fact]
    public void BranchUnknownTarget_AddsCon002()
    {
        var plan = ValidForkJoinPlan();
        var steps = plan.Steps.ToList();
        steps[0] = steps[0] with { Branches = new List<string> { "legal", "ghost" } };

        AssertHasError(Validator.Validate(plan with { Steps = steps }), "AWF-CON-002");
    }

    [Fact]
    public void InboundUnknownTarget_AddsCon003()
    {
        var plan = ValidForkJoinPlan();
        var steps = plan.Steps.ToList();
        var join = steps.Single(s => s.StepId == "merge");
        steps[steps.IndexOf(join)] = join with
        {
            InboundSteps = new List<string> { "legal", "ghost" },
        };

        AssertHasError(Validator.Validate(plan with { Steps = steps }), "AWF-CON-003");
    }

    [Fact]
    public void EmptyNextStepsEventKey_AddsCon007()
    {
        var plan = PlanWith(new AgentStepBlueprint
        {
            StepId = "only",
            TeamRole = "A",
            NextSteps = new Dictionary<string, string> { ["  "] = "END" },
        });

        AssertHasError(Validator.Validate(plan), "AWF-CON-007");
    }

    // ── Team binding ──────────────────────────────────────────────────────────

    [Fact]
    public void AgentStepWithNoTeamBinding_AddsTeam001()
    {
        var plan = PlanWith(new AgentStepBlueprint
        {
            StepId = "unbound",
            NextSteps = new Dictionary<string, string> { ["Done"] = "END" },
        });

        AssertHasError(Validator.Validate(plan), "AWF-TEAM-001");
    }

    [Fact]
    public void AgentStepWithStaticTeamId_NeedsNoRole()
    {
        var plan = PlanWith(new AgentStepBlueprint
        {
            StepId = "bound",
            AgentTeamId = Guid.NewGuid(),
            NextSteps = new Dictionary<string, string> { ["Done"] = "END" },
        });

        AssertNoError(Validator.Validate(plan), "AWF-TEAM-001");
    }

    [Fact]
    public void EmptyGuidTeamId_CountsAsUnbound()
    {
        var plan = PlanWith(new AgentStepBlueprint
        {
            StepId = "bound",
            AgentTeamId = Guid.Empty,
            NextSteps = new Dictionary<string, string> { ["Done"] = "END" },
        });

        AssertHasError(Validator.Validate(plan), "AWF-TEAM-001");
    }

    [Fact]
    public void ControlNodesNeedNoTeamBinding()
    {
        AssertNoError(Validator.Validate(ValidForkJoinPlan()), "AWF-TEAM-001");
    }
}
