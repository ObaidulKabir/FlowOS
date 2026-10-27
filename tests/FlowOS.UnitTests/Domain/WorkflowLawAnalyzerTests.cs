using System.Collections.Generic;
using System.Linq;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Services;
using Xunit;

namespace FlowOS.UnitTests.Domain;

/// <summary>
/// CR-16: the static Law check reports workflow exits the state machine can never allow, using the same
/// rules as the runtime (unknown events are ignored, allowed non-exit events change only the state,
/// Decisions route without an event, branches after a Fork share the state).
/// </summary>
public class WorkflowLawAnalyzerTests
{
    private static StateMachineBlueprint Machine(string initial, params (string From, string Event, string To)[] transitions) => new()
    {
        InitialState = initial,
        States = transitions.SelectMany(t => new[] { t.From, t.To }).Append(initial).Distinct().ToList(),
        Transitions = transitions.Select(t => new TransitionBlueprint { FromState = t.From, EventId = t.Event, ToState = t.To }).ToList()
    };

    private static StepBlueprint Step(string id, params (string Event, string Next)[] next) => new()
    {
        StepId = id,
        StepType = "HumanTask",
        NextSteps = next.ToDictionary(n => n.Event, n => n.Next)
    };

    private static IReadOnlyList<WorkflowLawAnalyzer.DeadExit> Analyze(StateMachineBlueprint machine, string start, params StepBlueprint[] steps) =>
        WorkflowLawAnalyzer.FindDeadExits(new WorkflowClassBlueprint
        {
            StateMachine = machine,
            Workflow = new WorkflowBlueprint { StartStepId = start, Steps = steps.ToList() }
        });

    private static readonly StateMachineBlueprint Review = Machine("Draft",
        ("Draft", "SUBMIT", "Submitted"), ("Submitted", "APPROVE", "Approved"), ("Submitted", "REJECT", "Draft"));

    [Fact]
    public void A_workflow_that_follows_the_state_machine_passes()
    {
        Assert.Empty(Analyze(Review, "Fill",
            Step("Fill", ("SUBMIT", "Review")),
            Step("Review", ("APPROVE", "END"), ("REJECT", "Fill"))));
    }

    [Fact]
    public void Submitting_twice_is_reported()
    {
        var machine = Machine("Draft", ("Draft", "SUBMIT", "Submitted"), ("Submitted", "APPROVE", "Approved"));

        var dead = Assert.Single(Analyze(machine, "Fill",
            Step("Fill", ("SUBMIT", "Confirm")),
            Step("Confirm", ("SUBMIT", "END"))));

        Assert.Equal("Confirm", dead.StepId);
        Assert.Equal("SUBMIT", dead.EventId);
        Assert.Equal(new[] { "Approved", "Submitted" }, dead.StatesAtStep);
        Assert.Equal(new[] { "Draft" }, dead.AllowedFromStates);
    }

    [Fact]
    public void An_exit_that_needs_a_state_reachable_by_a_state_only_event_is_allowed()
    {
        // At runtime SUBMIT can be published while on "Fill" (it is not Fill's exit): the state moves to
        // Submitted and the step stays, after which APPROVE is legal.
        Assert.Empty(Analyze(Review, "Fill", Step("Fill", ("APPROVE", "END"))));
    }

    [Fact]
    public void Events_the_state_machine_does_not_mention_move_the_step_and_keep_the_state()
    {
        var machine = Machine("Draft", ("Draft", "SUBMIT", "Submitted"), ("Submitted", "APPROVE", "Approved"));

        Assert.Empty(Analyze(machine, "Note",
            Step("Note", ("NOTE_ADDED", "Fill")),
            Step("Fill", ("SUBMIT", "END"))));
    }

    [Fact]
    public void Guarded_transitions_count_every_possible_target()
    {
        var machine = Machine("New", ("New", "SCORE", "Pass"), ("New", "SCORE", "Fail"), ("Pass", "CERTIFY", "Certified"));

        Assert.Empty(Analyze(machine, "Assess",
            Step("Assess", ("SCORE", "Certify")),
            Step("Certify", ("CERTIFY", "END"))));
    }

    [Fact]
    public void An_SLA_timeout_the_state_machine_never_allows_at_that_step_is_reported()
    {
        var machine = Machine("Draft", ("Draft", "SUBMIT", "Submitted"), ("Submitted", "APPROVE", "Approved"), ("Approved", "TIMEOUT", "Late"));
        var review = Step("Review", ("APPROVE", "END")) with { Sla = new StepSlaBlueprint { TimeoutEvent = "TIMEOUT", EscalationStepId = "Escalate" } };

        var dead = Assert.Single(Analyze(machine, "Fill", Step("Fill", ("SUBMIT", "Review")), review, Step("Escalate", ("APPROVE", "END"))));

        Assert.Equal(("Review", "TIMEOUT"), (dead.StepId, dead.EventId));
    }

    [Fact]
    public void After_a_fork_any_reachable_state_is_assumed()
    {
        var fork = new StepBlueprint { StepId = "Split", StepType = "Fork", Branches = new List<string> { "Left", "Right" } };

        Assert.Empty(Analyze(Review, "Start",
            Step("Start", ("NOTE", "Split")), fork,
            Step("Left", ("APPROVE", "END")), Step("Right", ("SUBMIT", "END"))));

        var unreachable = Machine("Draft", ("Draft", "SUBMIT", "Submitted"), ("Archived", "PURGE", "Gone"));
        var dead = Assert.Single(Analyze(unreachable, "Start",
            Step("Start", ("NOTE", "Split")), fork,
            Step("Left", ("PURGE", "END")), Step("Right", ("SUBMIT", "END"))));
        Assert.Equal(("Left", "PURGE"), (dead.StepId, dead.EventId));
    }

    [Fact]
    public void Decisions_route_without_changing_the_state()
    {
        var machine = Machine("Draft", ("Draft", "SUBMIT", "Submitted"), ("Submitted", "APPROVE", "Approved"));
        var route = Step("Route") with { StepType = "Decision", Conditions = new Dictionary<string, string> { ["amount > 100"] = "Approve", ["amount <= 100"] = "Resubmit" } };

        var dead = Assert.Single(Analyze(machine, "Fill",
            Step("Fill", ("SUBMIT", "Route")), route,
            Step("Approve", ("APPROVE", "END")),
            Step("Resubmit", ("SUBMIT", "END"))));

        Assert.Equal(("Resubmit", "SUBMIT"), (dead.StepId, dead.EventId));
    }

    [Fact]
    public void Missing_law_or_loops_do_not_break_the_analysis()
    {
        Assert.Empty(Analyze(new StateMachineBlueprint(), "A", Step("A", ("X", "END"))));
        Assert.Empty(Analyze(Review, "Fill", Step("Fill", ("SUBMIT", "Review")), Step("Review", ("REJECT", "Fill"), ("APPROVE", "END"))));
        Assert.Empty(Analyze(Review, "Fill", new StepBlueprint { StepId = "Fill", StepType = "HumanTask", NextSteps = null!, Conditions = null! }));
    }

    [Fact]
    public void The_validator_reports_LAW_001()
    {
        var machine = Machine("Draft", ("Draft", "SUBMIT", "Submitted"), ("Submitted", "APPROVE", "Approved"));
        var blueprint = new WorkflowClassBlueprint
        {
            StateMachine = machine,
            Events = new List<EventBlueprint> { new() { EventId = "SUBMIT" }, new() { EventId = "APPROVE" } },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Fill",
                Steps = new List<StepBlueprint> { Step("Fill", ("SUBMIT", "Confirm")), Step("Confirm", ("SUBMIT", "END")) }
            }
        };

        var result = new WorkflowClassValidator().Validate(blueprint);

        var error = Assert.Single(result.Errors, e => e.Code == "LAW-001");
        Assert.Equal("Law", error.Category);
        Assert.Contains("'Confirm'", error.Message);
    }
}
