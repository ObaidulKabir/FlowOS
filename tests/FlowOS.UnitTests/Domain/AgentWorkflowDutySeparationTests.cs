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
/// ADR-007: separation of duties.
///
/// <c>RoleBlueprint</c> resolves <em>who holds</em> a role. Nothing expressed relationships
/// <em>between</em> assignments, so "consequential" plus "anyone holding the capability"
/// permitted the approver to be whoever created the work — a weaker guarantee than the
/// system implies, and one that applies to every consequential step already shipped.
///
/// Constraints are validated structurally at publish, which is all that is knowable then.
/// Evaluation against actual holders happens at assignment time, with the Phase 4B engine.
/// </summary>
public class AgentWorkflowDutySeparationTests
{
    private static readonly AgentWorkflowBlueprintValidator Validator = new();

    // ── Builders ──────────────────────────────────────────────────────────────

    /// <summary>Two declared roles and a human gate, ready to constrain.</summary>
    private static AgentWorkflowBlueprint PlanWithRoles() => new()
    {
        Objective = "Code an invoice, then have someone other than the submitter release payment",
        StartStepId = "code-invoice",
        Capabilities = new List<CapabilityBlueprint>
        {
            new() { Code = "ap.payment.approve" },
            new() { Code = "ap.payment.review" },
        },
        Roles = new List<RoleBlueprint>
        {
            new() { Name = "PaymentApprover", GrantedCapabilities = new List<string> { "ap.payment.approve" } },
            new() { Name = "PaymentReviewer", GrantedCapabilities = new List<string> { "ap.payment.review" } },
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

    private static AgentWorkflowBlueprint Constrained(params AssignmentConstraintBlueprint[] constraints) =>
        PlanWithRoles() with { Constraints = constraints.ToList() };

    private static void AssertHasError(ValidationResult result, string code) =>
        Assert.Contains(code, result.Errors.Select(e => e.Code));

    private static void AssertNoError(ValidationResult result, string code) =>
        Assert.DoesNotContain(code, result.Errors.Select(e => e.Code));

    private static string Describe(ValidationResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}"));

    // ── Happy paths ───────────────────────────────────────────────────────────

    [Fact]
    public void PlanWithNoConstraints_IsValid()
    {
        // Declaring duty separation is optional.
        var result = Validator.Validate(PlanWithRoles());
        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void DistinctBetweenTwoDeclaredRoles_IsValid()
    {
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.Distinct,
            Between = new List<string> { "PaymentApprover", "PaymentReviewer" },
        });

        var result = Validator.Validate(plan);
        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void NotSubjectOfWithASubject_IsValid()
    {
        // The case this exists for: whoever submitted the invoice may not approve it.
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.NotSubjectOf,
            Between = new List<string> { "PaymentApprover" },
            Subject = "{{submittedBy}}",
        });

        var result = Validator.Validate(plan);
        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void SeniorToBetweenTwoRoles_IsValid()
    {
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.SeniorTo,
            Between = new List<string> { "PaymentApprover", "PaymentReviewer" },
        });

        var result = Validator.Validate(plan);
        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void AStepAssigneeIsAValidReference()
    {
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.Distinct,
            Between = new List<string> { "PaymentApprover", "approve-payment.assignee" },
        });

        AssertNoError(Validator.Validate(plan), "AWF-CON-008");
    }

    // ── Rule vocabulary ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("sameAs")]
    [InlineData("")]
    [InlineData("DISTINCTT")]
    public void UnknownRule_AddsSod001(string rule)
    {
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = rule,
            Between = new List<string> { "PaymentApprover", "PaymentReviewer" },
        });

        AssertHasError(Validator.Validate(plan), "AWF-SOD-001");
    }

    [Fact]
    public void RuleIsCaseInsensitive()
    {
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = "DISTINCT",
            Between = new List<string> { "PaymentApprover", "PaymentReviewer" },
        });

        AssertNoError(Validator.Validate(plan), "AWF-SOD-001");
    }

    // ── Arity ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DistinctWithOneReference_AddsSod002()
    {
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.Distinct,
            Between = new List<string> { "PaymentApprover" },
        });

        AssertHasError(Validator.Validate(plan), "AWF-SOD-002");
    }

    [Fact]
    public void DistinctBetweenTheSameReferenceTwice_AddsSod002()
    {
        // "PaymentApprover must differ from PaymentApprover" is unsatisfiable, so it is a
        // declaration error rather than a constraint that always fails at runtime.
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.Distinct,
            Between = new List<string> { "PaymentApprover", "paymentapprover" },
        });

        AssertHasError(Validator.Validate(plan), "AWF-SOD-002");
    }

    [Fact]
    public void NotSubjectOfWithoutASubject_AddsSod003()
    {
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.NotSubjectOf,
            Between = new List<string> { "PaymentApprover" },
        });

        AssertHasError(Validator.Validate(plan), "AWF-SOD-003");
    }

    [Fact]
    public void NotSubjectOfWithTwoReferences_AddsSod003()
    {
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.NotSubjectOf,
            Between = new List<string> { "PaymentApprover", "PaymentReviewer" },
            Subject = "{{submittedBy}}",
        });

        AssertHasError(Validator.Validate(plan), "AWF-SOD-003");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void SeniorToWithoutExactlyTwoReferences_AddsSod004(int count)
    {
        var refs = new[] { "PaymentApprover", "PaymentReviewer", "approve-payment.assignee" }
            .Take(count)
            .ToList();

        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.SeniorTo,
            Between = refs,
        });

        AssertHasError(Validator.Validate(plan), "AWF-SOD-004");
    }

    // ── Reference resolution ──────────────────────────────────────────────────

    [Fact]
    public void ReferenceToAnUndeclaredRole_AddsCon008()
    {
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.Distinct,
            Between = new List<string> { "PaymentApprover", "Nobody" },
        });

        AssertHasError(Validator.Validate(plan), "AWF-CON-008");
    }

    [Fact]
    public void ReferenceToAnUnknownStepAssignee_AddsCon008()
    {
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.Distinct,
            Between = new List<string> { "PaymentApprover", "ghost-step.assignee" },
        });

        AssertHasError(Validator.Validate(plan), "AWF-CON-008");
    }

    [Fact]
    public void BlankReference_AddsCon008()
    {
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.Distinct,
            Between = new List<string> { "PaymentApprover", "   " },
        });

        AssertHasError(Validator.Validate(plan), "AWF-CON-008");
    }

    [Fact]
    public void InstanceQualifiedReference_AddsCon008WithGuidance()
    {
        // Marker#0 syntax needs per-role instances, which do not exist yet. Rejected now
        // rather than silently treated as a role literally named "Marker#0".
        var plan = Constrained(new AssignmentConstraintBlueprint
        {
            Rule = AssignmentConstraintRules.Distinct,
            Between = new List<string> { "PaymentApprover#0", "PaymentApprover#1" },
        });

        var result = Validator.Validate(plan);
        AssertHasError(result, "AWF-CON-008");
        Assert.Contains("instance", Describe(result), StringComparison.OrdinalIgnoreCase);
    }

    // ── Several constraints ───────────────────────────────────────────────────

    [Fact]
    public void SeveralConstraintsAreAllValidated()
    {
        var plan = Constrained(
            new AssignmentConstraintBlueprint
            {
                Rule = AssignmentConstraintRules.Distinct,
                Between = new List<string> { "PaymentApprover", "PaymentReviewer" },
            },
            new AssignmentConstraintBlueprint
            {
                Rule = "nonsense",
                Between = new List<string> { "PaymentApprover", "PaymentReviewer" },
            });

        var result = Validator.Validate(plan);
        AssertHasError(result, "AWF-SOD-001");
        AssertNoError(result, "AWF-CON-008");
    }
}
