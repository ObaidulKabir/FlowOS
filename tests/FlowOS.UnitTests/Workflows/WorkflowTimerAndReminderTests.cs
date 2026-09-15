using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Services;
using FlowOS.Domain.Blueprints;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Services;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowOS.UnitTests.Workflows;

public class WorkflowTimerAndReminderTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task WorkflowTimerService_ScheduleTimerAtAsync_PersistsExactDueUtcTime()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase("FlowOS_ScheduleAt_Test_" + Guid.NewGuid())
            .Options;

        using var db = new FlowOSDbContext(options);
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var timerService = new WorkflowTimerService(db, serviceProvider, NullLogger<WorkflowTimerService>.Instance);

        var instanceId = Guid.NewGuid();
        var stepId = "ReviewStep";
        var exactDueUtc = DateTime.UtcNow.AddHours(24);

        await timerService.ScheduleTimerAtAsync(_tenantId, instanceId, stepId, exactDueUtc, "EVT-REMINDER-PRE");

        var job = await db.WorkflowTimerJobs.FirstOrDefaultAsync(t => t.WorkflowInstanceId == instanceId && t.StepId == stepId);
        Assert.NotNull(job);
        Assert.Equal("EVT-REMINDER-PRE", job!.TriggerEventType);
        Assert.Equal(exactDueUtc, job.DueTimeUtc);
        Assert.False(job.IsProcessed);
    }

    [Fact]
    public async Task WorkflowTimerService_CancelTimerAsync_CancelsAllSlaAndIntermediateRemindersForStep()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase("FlowOS_MultiCancel_Test_" + Guid.NewGuid())
            .Options;

        using var db = new FlowOSDbContext(options);
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var timerService = new WorkflowTimerService(db, serviceProvider, NullLogger<WorkflowTimerService>.Instance);

        var instanceId = Guid.NewGuid();
        var stepId = "ApprovalStep";

        // Schedule primary SLA timeout (48h)
        await timerService.ScheduleTimerAsync(_tenantId, instanceId, stepId, TimeSpan.FromHours(48), "EVT-SLA-TIMEOUT");

        // Schedule intermediate reminder 1 (-24h before SLA = 24h from now)
        await timerService.ScheduleTimerAsync(_tenantId, instanceId, stepId, TimeSpan.FromHours(24), "EVT-REMINDER-1");

        // Schedule intermediate reminder 2 (-2h before SLA = 46h from now)
        await timerService.ScheduleTimerAsync(_tenantId, instanceId, stepId, TimeSpan.FromHours(46), "EVT-REMINDER-2");

        var activeJobsBefore = await db.WorkflowTimerJobs
            .Where(t => t.WorkflowInstanceId == instanceId && t.StepId == stepId && !t.IsProcessed)
            .ToListAsync();
        Assert.Equal(3, activeJobsBefore.Count);

        // Cancel all timers for this step (simulating task completion)
        await timerService.CancelTimerAsync(instanceId, stepId);

        var activeJobsAfter = await db.WorkflowTimerJobs
            .Where(t => t.WorkflowInstanceId == instanceId && t.StepId == stepId && !t.IsProcessed)
            .ToListAsync();
        Assert.Empty(activeJobsAfter);

        var processedJobs = await db.WorkflowTimerJobs
            .Where(t => t.WorkflowInstanceId == instanceId && t.StepId == stepId && t.IsProcessed)
            .ToListAsync();
        Assert.Equal(3, processedJobs.Count);
    }

    [Fact]
    public void Validator_Rejects_InvalidReminderDuration_WF_SLA_003()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateBaseBlueprint();

        blueprint.Workflow.Steps[0].Sla = new StepSlaBlueprint
        {
            Duration = "24h",
            TimeoutEvent = "EVT-TIMEOUT",
            Reminders = new List<StepReminderBlueprint>
            {
                new() { Duration = "invalid_offset_time", TriggerEvent = "EVT-REMINDER" }
            }
        };

        var result = validator.Validate(blueprint);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-SLA-003");
    }

    [Fact]
    public void Validator_Rejects_UndeclaredReminderTriggerEvent_WF_SLA_004()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateBaseBlueprint();

        blueprint.Workflow.Steps[0].Sla = new StepSlaBlueprint
        {
            Duration = "24h",
            TimeoutEvent = "EVT-TIMEOUT",
            Reminders = new List<StepReminderBlueprint>
            {
                new() { Duration = "-2h", TriggerEvent = "EVT-NON-EXISTENT-REMINDER" }
            }
        };

        var result = validator.Validate(blueprint);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-SLA-004");
    }

    [Fact]
    public void Validator_Rejects_RelativeTimer_Without_TargetTimestampProperty_WF_TMR_001()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateBaseBlueprint();

        // Connect ApprovalStep to PreEventTimer so graph is reachable
        blueprint.Workflow.Steps[0].NextSteps["EVT-TIMER-EXPIRED"] = "PreEventTimer";

        blueprint.Workflow.Steps.Add(new StepBlueprint
        {
            StepId = "PreEventTimer",
            StepType = "Timer",
            Conditions = new Dictionary<string, string>
            {
                { "leadTime", "-24h" }
                // Missing targetTimestampProperty
            },
            NextSteps = new Dictionary<string, string>
            {
                { "EVT-TIMER-EXPIRED", "END" }
            }
        });

        var result = validator.Validate(blueprint);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "WF-TMR-001");
    }

    [Fact]
    public void Validator_Accepts_Valid_SlaReminders_And_RelativeTimers()
    {
        var validator = new WorkflowClassValidator();
        var blueprint = CreateBaseBlueprint();

        // Add valid SLA with -2h reminder
        blueprint.Workflow.Steps[0].Sla = new StepSlaBlueprint
        {
            Duration = "24h",
            TimeoutEvent = "EVT-TIMEOUT",
            Reminders = new List<StepReminderBlueprint>
            {
                new() { Duration = "-2h", TriggerEvent = "EVT-REMINDER" },
                new() { Duration = "30m", TriggerEvent = "EVT-REMINDER" }
            }
        };

        // Connect ApprovalStep to EventCountdownTimer so graph is reachable
        blueprint.Workflow.Steps[0].NextSteps["EVT-TIMER-EXPIRED"] = "EventCountdownTimer";

        // Add valid relative timer step
        blueprint.Workflow.Steps.Add(new StepBlueprint
        {
            StepId = "EventCountdownTimer",
            StepType = "Timer",
            Conditions = new Dictionary<string, string>
            {
                { "targetTimestampProperty", "appointmentDate" },
                { "leadTime", "-24h" }
            },
            NextSteps = new Dictionary<string, string>
            {
                { "EVT-TIMER-EXPIRED", "END" }
            }
        });

        var result = validator.Validate(blueprint);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public async Task WorkflowCopilotService_Generates_Reminders_When_Prompt_Mentions_Reminder()
    {
        var validator = new WorkflowClassValidator();
        var copilot = new WorkflowCopilotService(validator);
        var prompt = "Appointment booking workflow with reminder alert 24h before appointmentDate and 48h approval SLA";

        var response = await copilot.GenerateBlueprintAsync(prompt);

        Assert.True(response.Validation.IsValid, string.Join("; ", response.Validation.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Assert.NotNull(response.Blueprint);

        // Check that EVT-REMINDER is in declared events
        Assert.Contains(response.Blueprint.Events, e => e.EventId == "EVT-REMINDER");

        // Check that review step has SLA with reminders
        var reviewStep = response.Blueprint.Workflow.Steps.FirstOrDefault(s => s.StepId == "ReviewStep");
        Assert.NotNull(reviewStep);
        Assert.NotNull(reviewStep!.Sla);
        Assert.NotEmpty(reviewStep.Sla!.Reminders);
        Assert.Equal("-2h", reviewStep.Sla.Reminders[0].Duration);
        Assert.Equal("EVT-REMINDER", reviewStep.Sla.Reminders[0].TriggerEvent);
    }

    [Fact]
    public async Task StartWorkflow_WithRelativeTimerStep_SchedulesTimerAtTargetMinusLeadTime()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var context = new FlowOSDbContext(options);

        var tenantId = Guid.NewGuid();
        var def = new WorkflowDefinition(tenantId, "RelativeTimerWf", 1, "PreEventTimer");
        var timerStep = new WorkflowStepDefinition("PreEventTimer", WorkflowStepType.Timer)
        {
            Conditions = new Dictionary<string, string>
            {
                { "targetTimestampProperty", "appointmentDate" },
                { "leadTime", "-2h" }
            }
        };
        def.AddStep(timerStep);
        def.Publish();
        context.WorkflowDefinitions.Add(def);
        await context.SaveChangesAsync();

        var mockTimer = new Moq.Mock<FlowOS.Application.Common.Interfaces.IWorkflowTimerService>();
        DateTime? scheduledDueTime = null;
        mockTimer.Setup(m => m.ScheduleTimerAtAsync(
            tenantId,
            Moq.It.IsAny<Guid>(),
            "PreEventTimer",
            Moq.It.IsAny<DateTime>(),
            Moq.It.IsAny<string>(),
            Moq.It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, string, DateTime, string, CancellationToken>((t, inst, step, due, evt, ct) =>
            {
                scheduledDueTime = due;
            })
            .Returns(Task.CompletedTask);

        var handler = new FlowOS.Application.Handlers.WorkflowCommandHandlers(
            new FlowOS.Infrastructure.Persistence.Repositories.UnitOfWork(context),
            new Moq.Mock<FlowOS.Core.Interfaces.IEventRegistry>().Object,
            new Moq.Mock<FlowOS.Core.Interfaces.ICurrentUser>().Object,
            new Moq.Mock<FlowOS.Security.Interfaces.ICapabilityService>().Object,
            new FlowOS.Workflows.Engine.WorkflowEngine(new FlowOS.StateMachines.Engine.StateMachineEngine()),
            timerService: mockTimer.Object
        );

        var futureAppointment = DateTime.UtcNow.AddHours(24);
        var command = new FlowOS.Application.Commands.StartWorkflowCommand(
            TenantId: tenantId,
            WorkflowDefinitionId: def.Id,
            Payload: new Dictionary<string, object>
            {
                { "appointmentDate", futureAppointment.ToString("o") }
            }
        );

        var instanceId = await handler.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, instanceId);
        Assert.NotNull(scheduledDueTime);
        var expectedDue = futureAppointment.AddHours(-2);
        Assert.True(Math.Abs((scheduledDueTime.Value - expectedDue).TotalSeconds) < 10);
    }

    [Fact]
    public async Task StartWorkflow_WithStepSlaReminders_SchedulesIntermediateReminders()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var context = new FlowOSDbContext(options);

        var tenantId = Guid.NewGuid();
        var def = new WorkflowDefinition(tenantId, "SlaReminderWf", 1, "ReviewTask");
        var reviewStep = new WorkflowStepDefinition("ReviewTask", WorkflowStepType.HumanTask)
        {
            Sla = new StepSlaDefinition("24h", "EVT-SLA-TIMEOUT")
            {
                Reminders = new List<StepReminderDefinition>
                {
                    new("-2h", "EVT-REMINDER")
                }
            }
        };
        def.AddStep(reviewStep);
        def.Publish();
        context.WorkflowDefinitions.Add(def);
        await context.SaveChangesAsync();

        var scheduledTimers = new List<(string step, string evt, DateTime due)>();
        var mockTimer = new Moq.Mock<FlowOS.Application.Common.Interfaces.IWorkflowTimerService>();
        mockTimer.Setup(m => m.ScheduleTimerAtAsync(
            tenantId,
            Moq.It.IsAny<Guid>(),
            "ReviewTask",
            Moq.It.IsAny<DateTime>(),
            Moq.It.IsAny<string>(),
            Moq.It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, string, DateTime, string, CancellationToken>((t, inst, step, due, evt, ct) =>
            {
                scheduledTimers.Add((step, evt, due));
            })
            .Returns(Task.CompletedTask);

        var handler = new FlowOS.Application.Handlers.WorkflowCommandHandlers(
            new FlowOS.Infrastructure.Persistence.Repositories.UnitOfWork(context),
            new Moq.Mock<FlowOS.Core.Interfaces.IEventRegistry>().Object,
            new Moq.Mock<FlowOS.Core.Interfaces.ICurrentUser>().Object,
            new Moq.Mock<FlowOS.Security.Interfaces.ICapabilityService>().Object,
            new FlowOS.Workflows.Engine.WorkflowEngine(new FlowOS.StateMachines.Engine.StateMachineEngine()),
            timerService: mockTimer.Object
        );

        var command = new FlowOS.Application.Commands.StartWorkflowCommand(TenantId: tenantId, WorkflowDefinitionId: def.Id);
        var instanceId = await handler.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, instanceId);
        Assert.Equal(2, scheduledTimers.Count);

        // Primary SLA scheduled at 24h from now
        var timeoutTimer = scheduledTimers.FirstOrDefault(t => t.evt == "EVT-SLA-TIMEOUT");
        Assert.Equal("ReviewTask", timeoutTimer.step);
        var expectedSlaDue = DateTime.UtcNow.AddHours(24);
        Assert.True(Math.Abs((timeoutTimer.due - expectedSlaDue).TotalSeconds) < 10);

        // Intermediate reminder scheduled at 22h from now (24h - 2h)
        var reminderTimer = scheduledTimers.FirstOrDefault(t => t.evt == "EVT-REMINDER");
        Assert.Equal("ReviewTask", reminderTimer.step);
        var expectedReminderDue = DateTime.UtcNow.AddHours(22);
        Assert.True(Math.Abs((reminderTimer.due - expectedReminderDue).TotalSeconds) < 10);
    }

    private static WorkflowClassBlueprint CreateBaseBlueprint()
    {
        return new WorkflowClassBlueprint
        {
            Events = new List<EventBlueprint>
            {
                new() { EventId = "EVT-SUBMIT", Name = "Submit", Category = FlowOS.Domain.Enums.EventCategory.Human },
                new() { EventId = "EVT-TIMEOUT", Name = "SLA Timeout", Category = FlowOS.Domain.Enums.EventCategory.System },
                new() { EventId = "EVT-REMINDER", Name = "Reminder Alert", Category = FlowOS.Domain.Enums.EventCategory.System },
                new() { EventId = "EVT-TIMER-EXPIRED", Name = "Timer Expired", Category = FlowOS.Domain.Enums.EventCategory.System }
            },
            StateMachine = new StateMachineBlueprint
            {
                InitialState = "Submitted",
                States = new List<string> { "Submitted", "Escalated", "Completed" },
                Transitions = new List<TransitionBlueprint>
                {
                    new() { FromState = "Submitted", ToState = "Escalated", EventId = "EVT-TIMEOUT" },
                    new() { FromState = "Submitted", ToState = "Submitted", EventId = "EVT-REMINDER" },
                    new() { FromState = "Submitted", ToState = "Completed", EventId = "EVT-TIMER-EXPIRED" }
                }
            },
            Workflow = new WorkflowBlueprint
            {
                StartStepId = "ApprovalStep",
                Steps = new List<StepBlueprint>
                {
                    new()
                    {
                        StepId = "ApprovalStep",
                        StepType = "HumanTask",
                        NextSteps = new Dictionary<string, string>
                        {
                            { "EVT-TIMEOUT", "END" },
                            { "EVT-REMINDER", "ApprovalStep" }
                        }
                    }
                }
            },
            Roles = new List<RoleBlueprint>
            {
                new() { Name = "Approver", Description = "Approves requests" }
            },
            Capabilities = new List<CapabilityBlueprint>()
        };
    }
}
