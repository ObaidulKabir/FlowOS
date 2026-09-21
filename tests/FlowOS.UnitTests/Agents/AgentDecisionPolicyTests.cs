using FlowOS.Agents.Abstractions;
using FlowOS.Application.DTOs;
using FlowOS.Domain.Enums;
using System.Text.Json;

namespace FlowOS.UnitTests.Agents;

public class AgentDecisionPolicyTests
{
    public static IEnumerable<object?[]> PolicyCases()
    {
        yield return
        [
            "commit",
            "EVT-ACCEPT",
            0.95,
            true,
            AgentDecisionKind.Commit,
            "In bounds.",
            "EVT-ACCEPT"
        ];
        yield return
        [
            "low-confidence park",
            "EVT-ACCEPT",
            0.40,
            true,
            AgentDecisionKind.Park,
            "minConfidence",
            "EVT-ACCEPT"
        ];
        yield return
        [
            "illegal event",
            "EVT-ILLEGAL",
            0.99,
            true,
            AgentDecisionKind.Park,
            "No legal suggestion",
            null
        ];
        yield return
        [
            "legal but not allowed",
            "EVT-REQUEST-REVISION",
            0.99,
            true,
            AgentDecisionKind.Park,
            "autoCommit.allowedEvents",
            "EVT-REQUEST-REVISION"
        ];
        yield return
        [
            "disabled agent",
            null,
            1.0,
            false,
            AgentDecisionKind.Skipped,
            "disabled",
            null
        ];
    }

    [Theory]
    [MemberData(nameof(PolicyCases))]
    public void LiveAndDeterministicEvaluation_SharePolicyOutcomes(
        string _,
        string? eventType,
        double confidence,
        bool enabled,
        AgentDecisionKind expectedKind,
        string expectedReason,
        string? expectedCandidate)
    {
        var packet = Packet();
        var simulation = AgentDecisionPolicy.EvaluateSimulation(
            packet,
            enabled,
            enabled
                ? new AgentSimulationSuggestion(eventType, confidence, "sim-agent")
                : null);

        Assert.Equal(expectedKind, simulation.Evaluation.Kind);
        Assert.Contains(expectedReason, simulation.Evaluation.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedCandidate, simulation.Candidate?.EventType);

        if (!enabled)
            return;

        var live = AgentDecisionPolicy.Evaluate(
            packet,
            AgentResult.WithActions(
                "provider result",
                [new SuggestedAction(eventType!, "provider suggestion", confidence)]),
            "live-agent");

        Assert.Equal(simulation.Evaluation.Kind, live.Evaluation.Kind);
        Assert.Equal(simulation.Evaluation.Reason, live.Evaluation.Reason);
        Assert.Equal(simulation.Candidate?.EventType, live.Candidate?.EventType);
    }

    [Fact]
    public void Evaluation_FiltersIllegalActionsBeforeSelectingHighestConfidenceCandidate()
    {
        var result = AgentResult.WithActions(
            "provider result",
            [
                new SuggestedAction("EVT-ILLEGAL", "highest but illegal", 0.99),
                new SuggestedAction("EVT-REQUEST-REVISION", "legal but not allowed", 0.96),
                new SuggestedAction("EVT-ACCEPT", "legal and allowed", 0.95)
            ]);

        var decision = AgentDecisionPolicy.Evaluate(Packet(), result);

        Assert.Equal("EVT-REQUEST-REVISION", decision.Candidate?.EventType);
        Assert.Equal(AgentDecisionKind.Park, decision.Evaluation.Kind);
        Assert.DoesNotContain(
            decision.Result.SuggestedActions,
            action => action.EventType == "EVT-ILLEGAL");
    }

    [Fact]
    public void ContextSimulationRequest_JsonDefaultEnablesDeterministicAgents()
    {
        var request = JsonSerializer.Deserialize<WorkflowContextSimulationRequest>(
            """{"contextType":"Quote","revision":"draft"}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(request);
        Assert.True(request.AutoAdvanceAgents);
    }

    private static DecisionPacket Packet() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "AgentReview",
            "Queued",
            "HumanTask",
            StepActor.Agent,
            "Review the quote.",
            null,
            new Dictionary<string, object?>(),
            ["EVT-ACCEPT", "EVT-REQUEST-REVISION", "EVT-TIMEOUT"],
            ["EVT-ACCEPT", "EVT-REQUEST-REVISION", "EVT-TIMEOUT"],
            Array.Empty<string>(),
            Array.Empty<SlaReminderFact>(),
            "EVT-TIMEOUT",
            new Dictionary<string, object>(),
            "Decide",
            new AutoCommitPolicy(0.9, ["EVT-ACCEPT"]));
}
