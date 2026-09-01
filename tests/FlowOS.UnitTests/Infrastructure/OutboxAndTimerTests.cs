using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Models;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.BackgroundServices;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.Notifications.Domain;
using FlowOS.Notifications.Infrastructure.Persistence;
using FlowOS.Workflows.Domain;
using FlowOS.Workflows.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FlowOS.UnitTests.Infrastructure;

public class OutboxAndTimerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task EventPublishingInterceptor_Creates_OutboxMessage_On_Event_Save()
    {
        var mockPublisher = new Mock<IPublisher>();
        var interceptor = new EventPublishingInterceptor(mockPublisher.Object);

        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase("FlowOS_Outbox_Test_" + Guid.NewGuid())
            .AddInterceptors(interceptor)
            .Options;

        using var db = new FlowOSDbContext(options);

        var domainEvent = new StandardEvent(_tenantId, "EVT-OUTBOX-TEST");
        domainEvent.SetCorrelationId(Guid.NewGuid());
        domainEvent.AddMetadata("Message", "Testing outbox");

        db.Events.Add(domainEvent);
        await db.SaveChangesAsync();

        // Verify that an OutboxMessage was automatically created in the exact same SaveChangesAsync call!
        var outboxMessage = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Type == "EVT-OUTBOX-TEST");
        Assert.NotNull(outboxMessage);
        Assert.Equal(_tenantId, outboxMessage!.TenantId);
        Assert.Null(outboxMessage.ProcessedOnUtc);
        Assert.Equal(0, outboxMessage.RetryCount);
    }

    [Fact]
    public async Task WorkflowTimerService_Schedules_And_Cancels_Timer_Jobs()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase("FlowOS_Timer_Test_" + Guid.NewGuid())
            .Options;

        using var db = new FlowOSDbContext(options);
        var timerService = new WorkflowTimerService(db, NullLogger<WorkflowTimerService>.Instance);

        var instanceId = Guid.NewGuid();
        var duration = TimeSpan.FromSeconds(30);

        // Schedule timer
        await timerService.ScheduleTimerAsync(_tenantId, instanceId, "WaitStep", duration, "EVT-TIME-OUT");

        var scheduledJob = await db.WorkflowTimerJobs.FirstOrDefaultAsync(t => t.WorkflowInstanceId == instanceId);
        Assert.NotNull(scheduledJob);
        Assert.Equal("WaitStep", scheduledJob!.StepId);
        Assert.Equal("EVT-TIME-OUT", scheduledJob.TriggerEventType);
        Assert.False(scheduledJob.IsProcessed);
        Assert.True(scheduledJob.DueTimeUtc > DateTime.UtcNow.AddSeconds(25));

        // Cancel timer
        await timerService.CancelTimerAsync(instanceId, "WaitStep");

        var cancelledJob = await db.WorkflowTimerJobs.FirstOrDefaultAsync(t => t.WorkflowInstanceId == instanceId);
        Assert.NotNull(cancelledJob);
        Assert.True(cancelledJob!.IsProcessed);
    }

    [Fact]
    public async Task OutboxMessage_Model_State_Transitions()
    {
        var outbox = new OutboxMessage(_tenantId, "EVT-TEST", "{}");
        Assert.Null(outbox.ProcessedOnUtc);
        Assert.Equal(0, outbox.RetryCount);

        outbox.RecordFailure("Network timeout");
        Assert.Equal(1, outbox.RetryCount);
        Assert.Equal("Network timeout", outbox.Error);

        outbox.MarkAsProcessed();
        Assert.NotNull(outbox.ProcessedOnUtc);
        Assert.Null(outbox.Error);
    }

    [Fact]
    public async Task WorkflowTimerJob_Model_State_Transitions()
    {
        var instanceId = Guid.NewGuid();
        var dueTime = DateTime.UtcNow.AddMinutes(5);
        var job = new WorkflowTimerJob(_tenantId, instanceId, "Step1", "EVT-WAKE", dueTime);

        Assert.Equal(instanceId, job.WorkflowInstanceId);
        Assert.Equal("Step1", job.StepId);
        Assert.Equal("EVT-WAKE", job.TriggerEventType);
        Assert.False(job.IsProcessed);
        Assert.Null(job.ProcessedAt);

        job.MarkAsProcessed();
        Assert.True(job.IsProcessed);
        Assert.NotNull(job.ProcessedAt);
    }
}
