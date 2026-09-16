using System;
using System.Collections.Generic;
using FlowOS.Application.Services;
using FlowOS.Domain.Blueprints;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class SlaSimulationClockTests
{
    [Fact]
    public void Plan_PositiveReminderOffsets_AreRelativeToStepEntry()
    {
        var sla = new StepSlaBlueprint
        {
            Duration = "24h",
            TimeoutEvent = "QUOTE_RESPONSE_OVERDUE",
            Reminders =
            [
                new() { Duration = "12h", TriggerEvent = "QUOTE_REMINDER_SENT" },
                new() { Duration = "2h", TriggerEvent = "QUOTE_REMINDER_SENT" }
            ]
        };

        var plan = SlaSimulationClock.Plan(sla);
        Assert.Equal(3, plan.Count);
        Assert.Equal(TimeSpan.FromHours(2), plan[0].DueAfter);
        Assert.Equal(SlaSimulationClock.ReminderKind, plan[0].Kind);
        Assert.Equal(TimeSpan.FromHours(12), plan[1].DueAfter);
        Assert.Equal(TimeSpan.FromHours(24), plan[2].DueAfter);
        Assert.Equal(SlaSimulationClock.TimeoutKind, plan[2].Kind);
        Assert.Equal("QUOTE_RESPONSE_OVERDUE", plan[2].EventType);
    }

    [Fact]
    public void Plan_NegativeReminderOffsets_AreRelativeToTimeout()
    {
        var sla = new StepSlaBlueprint
        {
            Duration = "24h",
            TimeoutEvent = "EVT-TIMEOUT",
            Reminders = [new() { Duration = "-2h", TriggerEvent = "EVT-WARN" }]
        };

        var plan = SlaSimulationClock.Plan(sla);
        Assert.Equal(2, plan.Count);
        Assert.Equal(TimeSpan.FromHours(22), plan[0].DueAfter);
        Assert.Equal("EVT-WARN", plan[0].EventType);
        Assert.Equal(TimeSpan.FromHours(24), plan[1].DueAfter);
    }

    [Fact]
    public void SelectForSimulation_CompletingEvent_FiresRemindersNotTimeout()
    {
        var plan = SlaSimulationClock.Plan("24h", "QUOTE_RESPONSE_OVERDUE", new[]
        {
            ("2h", "QUOTE_REMINDER_SENT"),
            ("12h", "QUOTE_REMINDER_SENT")
        });

        var selected = SlaSimulationClock.SelectForSimulation(
            plan,
            completingEventQueued: true,
            autoAdvanceTimers: false,
            alreadyQueuedEvents: Array.Empty<string>());

        Assert.Equal(2, selected.Count);
        Assert.All(selected, job => Assert.Equal(SlaSimulationClock.ReminderKind, job.Kind));
    }

    [Fact]
    public void SelectForSimulation_AutoAdvanceWithoutCompletingEvent_FiresTimeout()
    {
        var plan = SlaSimulationClock.Plan("8h", "REPAIR_OVERDUE", new[]
        {
            ("2h", "REPAIR_REMINDER_SENT"),
            ("6h", "REPAIR_REMINDER_SENT")
        });

        var selected = SlaSimulationClock.SelectForSimulation(
            plan,
            completingEventQueued: false,
            autoAdvanceTimers: true,
            alreadyQueuedEvents: Array.Empty<string>());

        Assert.Equal(3, selected.Count);
        Assert.Equal("REPAIR_OVERDUE", selected[^1].EventType);
    }

    [Fact]
    public void SelectForSimulation_SkipsEventsAlreadyQueued()
    {
        var plan = SlaSimulationClock.Plan("48h", "EVT-ESCALATE", new[]
        {
            ("-24h", "EVT-WARN-24H")
        });

        var selected = SlaSimulationClock.SelectForSimulation(
            plan,
            completingEventQueued: true,
            autoAdvanceTimers: false,
            alreadyQueuedEvents: new List<string> { "EVT-WARN-24H", "EVT-APPROVE" });

        Assert.Empty(selected);
    }
}
