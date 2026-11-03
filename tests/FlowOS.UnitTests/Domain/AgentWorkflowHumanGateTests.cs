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
/// ADR-007: the human gate in a declarative agent plan.
///
/// Before this, <c>Consequential = true</c> declared that an agent must not commit a step
/// while giving the plan no way to name who decides instead — the governance primitive had
/// nothing to hand off to. These tests pin the rules that make it operative: a plan can
/// declare human roles and capabilities, a step can be a HumanTask bound to one of them,
/// and a consequential step that nobody could ever commit is rejected rather than published.
/// </summary>
public class AgentWorkflowHumanGateTests
{
    private static readonly AgentWorkflowBlueprintValidator Validator = new();

    // ── Builders ──────────────────────────────────────────────────────────────

    /// <summary>An agent step that hands off to a human gate, which is the whole point.</summary>
    private static AgentWorkflowBlueprint PlanWithGate() => new()
    {
        Objective = "Code an invoice, then have a person release payment",
        StartStepId = "code-invoice",
        Capabilities = new List<CapabilityBlueprint>
        {
            new() { Code = "ap.payment.approve", Description = "Release a supplier payment" },
        },
        Roles = new List<RoleBlueprint>
        {
            new()
            {
                Name = "PaymentApprover",
                GrantedCapabilities = new List<string> { "ap.payment.approve" },
            },
        },
        Steps = new List<AgentStepBlueprint>
        {
            new()
            {
                StepId = "code-invoice",
                TeamRole = "InvoiceCoder",
                NextSteps = new Dictionary<string, string> { ["Coded"] = "approve-payment" },
            },
            new()
            {
                StepId = "approve-payment",
                StepType = AgentStepType.HumanTask,
                Role = "PaymentApprover",
                RequiredCapabilities = new List<string> { "ap.payment.approve" },
                Consequential = true,
                NextSteps = new Dictionary<string, string> { ["Approved"] = "END" },
            },
        },
    };

    private static AgentWorkflowBlueprint With(
        AgentWorkflowBlueprint plan,
        int index,
        Func<AgentStepBlueprint, AgentStepBlueprint> mutate)
    {
        var steps = plan.Steps.ToList();
        steps[index] = mutate(steps[index]);
        return plan with { Steps = steps };
    }

    private static void AssertHasError(ValidationResult result, string code) =>
        Assert.Contains(code, result.Errors.Select(e => e.Code));

    private static void AssertNoError(ValidationResult result, string code) =>
        Assert.DoesNotContain(code, result.Errors.Select(e => e.Code));

    private static string Describe(ValidationResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}"));

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void PlanWithAHumanGate_IsValid()
    {
        var result = Validator.Validate(PlanWithGate());
        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void HumanTaskNeedsNoTeamBinding()
    {
        // A HumanTask is carried out by a person, so it binds no agent team — the same
        // reasoning that exempts Fork, Join and End.
        AssertNoError(Validator.Validate(PlanWithGate()), "AWF-TEAM-001");
    }

    // ── The rule that makes Consequential operative ───────────────────────────

    [Fact]
    public void ConsequentialAgentStepWithNoHumanPath_AddsAgent006()
    {
        // An Agent-actor step that may never be auto-committed, and which no person can
        // complete, can never advance. Publishing it would deadlock every execution.
        var plan = PlanWithGate();
        var broken = With(plan, 0, s => s with { Consequential = true, Actor = StepActor.Agent });

        AssertHasError(Validator.Validate(broken), "AWF-AGENT-006");
    }

    [Theory]
    [InlineData(StepActor.Human)]
    [InlineData(StepActor.Either)]
    public void ConsequentialStepAPersonCanComplete_IsAccepted(string actor)
    {
        var plan = PlanWithGate();
        var ok = With(plan, 0, s => s with { Consequential = true, Actor = actor });

        AssertNoError(Validator.Validate(ok), "AWF-AGENT-006");
    }

    [Fact]
    public void ConsequentialHumanTask_IsAccepted()
    {
        // The gate in PlanWithGate is itself consequential and must not trip the rule.
        AssertNoError(Validator.Validate(PlanWithGate()), "AWF-AGENT-006");
    }

    [Fact]
    public void UnknownActor_AddsStr006()
    {
        var plan = With(PlanWithGate(), 0, s => s with { Actor = "Robot" });
        AssertHasError(Validator.Validate(plan), "AWF-STR-006");
    }

    // ── HumanTask shape ───────────────────────────────────────────────────────

    [Fact]
    public void HumanTaskWithoutARole_AddsHuman001()
    {
        var plan = With(PlanWithGate(), 1, s => s with { Role = null });
        AssertHasError(Validator.Validate(plan), "AWF-HUMAN-001");
    }

    [Fact]
    public void HumanTaskWithUndeclaredRole_AddsHuman002()
    {
        var plan = With(PlanWithGate(), 1, s => s with { Role = "Nobody" });
        AssertHasError(Validator.Validate(plan), "AWF-HUMAN-002");
    }

    [Fact]
    public void HumanTaskThatAlsoNamesAnAgentRole_AddsHuman003()
    {
        // A step is carried out by a person or by an agent team, never both.
        var plan = With(PlanWithGate(), 1, s => s with { TeamRole = "InvoiceCoder" });
        AssertHasError(Validator.Validate(plan), "AWF-HUMAN-003");
    }

    [Fact]
    public void HumanTaskStillNeedsAnExitPath()
    {
        var plan = With(PlanWithGate(), 1, s => s with { NextSteps = new Dictionary<string, string>() });
        AssertHasError(Validator.Validate(plan), "AWF-COMP-004");
    }

    // ── Capabilities ──────────────────────────────────────────────────────────

    [Fact]
    public void StepRequiringUndeclaredCapability_AddsGov001()
    {
        var plan = With(PlanWithGate(), 1, s => s with
        {
            RequiredCapabilities = new List<string> { "ap.payment.approve", "ap.ledger.post" },
        });

        AssertHasError(Validator.Validate(plan), "AWF-GOV-001");
    }

    [Fact]
    public void RoleGrantingUndeclaredCapability_AddsGov002()
    {
        var plan = PlanWithGate();
        var broken = plan with
        {
            Roles = new List<RoleBlueprint>
            {
                new()
                {
                    Name = "PaymentApprover",
                    GrantedCapabilities = new List<string> { "ap.payment.approve", "ap.invented" },
                },
            },
        };

        AssertHasError(Validator.Validate(broken), "AWF-GOV-002");
    }

    [Fact]
    public void DuplicateRoleName_AddsGov003()
    {
        var plan = PlanWithGate();
        var broken = plan with
        {
            Roles = new List<RoleBlueprint>
            {
                new() { Name = "PaymentApprover", GrantedCapabilities = new List<string> { "ap.payment.approve" } },
                new() { Name = "paymentapprover", GrantedCapabilities = new List<string> { "ap.payment.approve" } },
            },
        };

        AssertHasError(Validator.Validate(broken), "AWF-GOV-003");
    }

    [Fact]
    public void PlanWithNoRolesOrCapabilities_IsStillValid()
    {
        // Declaring governance is optional; a plan of pure agent steps needs none. The
        // checks only bite once a step references something.
        var plan = new AgentWorkflowBlueprint
        {
            Objective = "Agent-only plan",
            StartStepId = "only",
            Steps = new List<AgentStepBlueprint>
            {
                new()
                {
                    StepId = "only",
                    TeamRole = "Worker",
                    NextSteps = new Dictionary<string, string> { ["Done"] = "END" },
                },
            },
        };

        var result = Validator.Validate(plan);
        Assert.True(result.IsValid, Describe(result));
    }

    // ── Reachability still reaches through a gate ─────────────────────────────

    [Fact]
    public void AHumanTaskIsReachableAndRoutesOnward()
    {
        var result = Validator.Validate(PlanWithGate());
        AssertNoError(result, "AWF-COMP-003");
        AssertNoError(result, "AWF-COMP-004");
    }
}
