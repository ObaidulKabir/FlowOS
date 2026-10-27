using System;
using System.Collections.Generic;
using System.Linq;
using FlowOS.Application.DTOs;
using FlowOS.Application.Handlers;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Services;
using FlowOS.Workflows.Domain;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

/// <summary>CR-12: task priority rules.</summary>
public class TaskPriorityTests
{
    private static readonly DateTime T0 = new(2026, 10, 27, 8, 0, 0, DateTimeKind.Utc);

    private static TaskDto Task(string name, int priority, DateTime? due, int ageMinutes) => new()
    {
        TaskId = new Guid(name.PadLeft(32, '0')),
        CurrentStep = name,
        Priority = priority,
        DueAtUtc = due,
        CreatedAt = T0.AddMinutes(-ageMinutes)
    };

    [Fact]
    public void Inbox_order_is_priority_then_due_time_then_age_then_id()
    {
        var tasks = new[]
        {
            Task("1", 10, T0.AddHours(1), 5),
            Task("2", 50, null, 100),
            Task("3", 50, T0.AddHours(3), 1),
            Task("4", 50, T0.AddHours(2), 1),
            Task("5", 50, T0.AddHours(2), 30),
            Task("6", 50, null, 200),
        };

        var order = TaskQueryHandlers.OrderForInbox(tasks).Select(t => t.CurrentStep).ToList();

        // 50s first; within them the earliest due (older first on a tie); tasks without an SLA after those with one.
        Assert.Equal(new[] { "5", "4", "3", "6", "2", "1" }, order);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(null)]
    public void Instance_accepts_0_to_100_or_null(int? priority)
    {
        var instance = new WorkflowInstance(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 1, "Start");

        instance.SetPriority(priority);

        Assert.Equal(priority, instance.Priority);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Instance_rejects_priority_outside_0_to_100(int priority)
    {
        var instance = new WorkflowInstance(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 1, "Start");

        Assert.Throws<ArgumentOutOfRangeException>(() => instance.SetPriority(priority));
    }

    [Fact]
    public void Validator_rejects_step_priority_outside_0_to_100()
    {
        var blueprint = new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint> { new() { EventId = "EVT-DONE", Name = "Done" } },
            StateMachine = new StateMachineBlueprint { InitialState = "Open", States = new List<string> { "Open" } },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "Review",
                Steps = new List<StepBlueprint>
                {
                    new() { StepId = "Review", StepType = "HumanTask", Priority = 150, NextSteps = new Dictionary<string, string> { ["EVT-DONE"] = "END" } }
                }
            }
        };

        var errors = new WorkflowClassValidator().Validate(blueprint).Errors;

        Assert.Contains(errors, e => e.Code == "WF-TASK-001" && e.Message.Contains("'Review'"));
    }
}
