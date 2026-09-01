using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Models;
using FlowOS.Core.Interfaces;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using FlowOS.Infrastructure.Services;
using FlowOS.Notifications.Api;
using FlowOS.Notifications.Application;
using FlowOS.Notifications.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FlowOS.UnitTests.Notifications;

public class NotificationCapabilityTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId1 = Guid.NewGuid();
    private readonly Guid _userId2 = Guid.NewGuid();

    [Fact]
    public void Notification_DomainModel_Creation_And_MarkAsRead()
    {
        var correlationId = Guid.NewGuid();
        var notif = new Notification(_tenantId, "EVT-TEST", "Hello World", "Warning", correlationId, _userId1);

        Assert.NotEqual(Guid.Empty, notif.Id);
        Assert.Equal(_tenantId, notif.TenantId);
        Assert.Equal("EVT-TEST", notif.EventType);
        Assert.Equal("Hello World", notif.Message);
        Assert.Equal("Warning", notif.Severity);
        Assert.Equal(correlationId, notif.CorrelationId);
        Assert.Equal(_userId1, notif.TargetUserId);
        Assert.False(notif.IsRead);

        notif.MarkAsRead();
        Assert.True(notif.IsRead);
    }

    [Fact]
    public async Task NotificationProjector_Maps_Custom_Metadata_Correctly()
    {
        var mockRepo = new Mock<INotificationRepository>();
        Notification? savedNotification = null;
        mockRepo.Setup(r => r.Add(It.IsAny<Notification>()))
                .Callback<Notification>(n => savedNotification = n);
        mockRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var streamService = new NotificationStreamService();
        var projector = new NotificationProjector(mockRepo.Object, streamService, NullLogger<NotificationProjector>.Instance);

        var domainEvent = new StandardEvent(_tenantId, "EVT-CUSTOM-ALERT");
        domainEvent.SetCorrelationId(Guid.NewGuid());
        domainEvent.AddMetadata("Message", "Custom alert occurred");
        domainEvent.AddMetadata("Severity", "Critical");
        domainEvent.AddMetadata("TargetUserId", _userId1.ToString());

        var notificationMsg = new DomainEventNotification<DomainEvent>(domainEvent);
        await projector.Handle(notificationMsg, CancellationToken.None);

        Assert.NotNull(savedNotification);
        Assert.Equal("Custom alert occurred", savedNotification!.Message);
        Assert.Equal("Critical", savedNotification.Severity);
        Assert.Equal(_userId1, savedNotification.TargetUserId);
        Assert.Equal("EVT-CUSTOM-ALERT", savedNotification.EventType);
    }

    [Fact]
    public async Task NotificationProjector_Maps_AssignedTo_Metadata_Correctly()
    {
        var mockRepo = new Mock<INotificationRepository>();
        Notification? savedNotification = null;
        mockRepo.Setup(r => r.Add(It.IsAny<Notification>()))
                .Callback<Notification>(n => savedNotification = n);
        mockRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var streamService = new NotificationStreamService();
        var projector = new NotificationProjector(mockRepo.Object, streamService, NullLogger<NotificationProjector>.Instance);

        var domainEvent = new StandardEvent(_tenantId, "EVT-TASK-ASSIGNED");
        domainEvent.SetCorrelationId(Guid.NewGuid());
        domainEvent.AddMetadata("AssignedTo", _userId2.ToString());

        var notificationMsg = new DomainEventNotification<DomainEvent>(domainEvent);
        await projector.Handle(notificationMsg, CancellationToken.None);

        Assert.NotNull(savedNotification);
        Assert.Equal("Task assigned to you", savedNotification!.Message);
        Assert.Equal("Info", savedNotification.Severity);
        Assert.Equal(_userId2, savedNotification.TargetUserId);
    }

    [Fact]
    public async Task NotificationStreamService_Broadcasts_To_TargetUser_Or_All()
    {
        var streamService = new NotificationStreamService();

        var writer1 = new StringWriter();
        var writer2 = new StringWriter();

        using var client1 = new StreamClient(writer1, _userId1);
        using var client2 = new StreamClient(writer2, _userId2);

        streamService.AddClient(_tenantId, client1);
        streamService.AddClient(_tenantId, client2);

        // 1. Notification targeted only at User 1
        var targetedNotif = new Notification(_tenantId, "EVT-USER1", "For User 1 Only", "Info", null, _userId1);
        await streamService.BroadcastAsync(targetedNotif);

        var output1 = writer1.ToString();
        var output2 = writer2.ToString();

        Assert.Contains("For User 1 Only", output1);
        Assert.DoesNotContain("For User 1 Only", output2);

        // 2. Global notification (TargetUserId is null)
        var globalNotif = new Notification(_tenantId, "EVT-GLOBAL", "Broadcast To All", "Warning", null, null);
        await streamService.BroadcastAsync(globalNotif);

        output1 = writer1.ToString();
        output2 = writer2.ToString();

        Assert.Contains("Broadcast To All", output1);
        Assert.Contains("Broadcast To All", output2);
    }

    [Fact]
    public async Task NotificationStreamService_Concurrent_Broadcasts_Are_ThreadSafe()
    {
        var streamService = new NotificationStreamService();
        var writer = new StringWriter();
        using var client = new StreamClient(writer, _userId1);
        streamService.AddClient(_tenantId, client);

        var tasks = Enumerable.Range(1, 50).Select(i =>
        {
            var notif = new Notification(_tenantId, $"EVT-{i}", $"Message #{i}", "Info", null, _userId1);
            return streamService.BroadcastAsync(notif);
        });

        await Task.WhenAll(tasks);

        var output = writer.ToString();
        for (int i = 1; i <= 50; i++)
        {
            Assert.Contains($"Message #{i}", output);
        }
    }

    [Fact]
    public async Task NotificationRepository_GetNotifications_And_MarkAsRead_Works()
    {
        var options = new DbContextOptionsBuilder<FlowOSDbContext>()
            .UseInMemoryDatabase("FlowOS_Notification_Repo_Test_" + Guid.NewGuid())
            .Options;

        using var db = new FlowOSDbContext(options);
        var repo = new NotificationRepository(db);

        // Seed notifications
        var n1 = new Notification(_tenantId, "EVT-1", "User 1 notification", "Info", null, _userId1);
        var n2 = new Notification(_tenantId, "EVT-2", "User 2 notification", "Warning", null, _userId2);
        var nGlobal = new Notification(_tenantId, "EVT-3", "Global notification", "Critical", null, null);
        var nOtherTenant = new Notification(Guid.NewGuid(), "EVT-4", "Other tenant notification", "Info", null, _userId1);

        repo.Add(n1);
        repo.Add(n2);
        repo.Add(nGlobal);
        repo.Add(nOtherTenant);
        await repo.SaveChangesAsync(CancellationToken.None);

        // Query for User 1
        var user1ResultsObj = await repo.GetNotificationsAsync(_tenantId, _userId1);
        var user1Results = Assert.IsAssignableFrom<IEnumerable<object>>(user1ResultsObj).ToList();

        Assert.Equal(2, user1Results.Count); // n1 and nGlobal

        // Mark n1 as read
        await repo.MarkAsReadAsync(_tenantId, _userId1, n1.Id);

        // Verify in DB that n1 is read
        var reloadedN1 = await db.Notifications.FindAsync(n1.Id);
        Assert.NotNull(reloadedN1);
        Assert.True(reloadedN1!.IsRead);

        // Ensure user cannot mark another user's notification as read
        await repo.MarkAsReadAsync(_tenantId, _userId1, n2.Id);
        var reloadedN2 = await db.Notifications.FindAsync(n2.Id);
        Assert.NotNull(reloadedN2);
        Assert.False(reloadedN2!.IsRead);
    }

    [Fact]
    public async Task NotificationsController_Get_And_MarkAsRead_Endpoints()
    {
        var mockQueryService = new Mock<INotificationQueryService>();
        var notifId = Guid.NewGuid();

        mockQueryService.Setup(s => s.GetNotificationsAsync(_tenantId, _userId1))
            .ReturnsAsync(new List<object>
            {
                new { Id = notifId, Message = "Test Message", Severity = "Info", CreatedAt = DateTime.UtcNow, EventType = "EVT-1", IsRead = false }
            });

        mockQueryService.Setup(s => s.MarkAsReadAsync(_tenantId, _userId1, notifId))
            .Returns(Task.CompletedTask);

        var mockCurrentUser = new Mock<ICurrentUser>();
        mockCurrentUser.Setup(u => u.TenantId).Returns(_tenantId);
        mockCurrentUser.Setup(u => u.Id).Returns(_userId1.ToString());

        var streamService = new NotificationStreamService();
        var controller = new NotificationsController(mockQueryService.Object, mockCurrentUser.Object, streamService);

        // 1. Test GetNotifications
        var getResult = await controller.GetNotifications();
        var okResult = Assert.IsType<OkObjectResult>(getResult);
        Assert.NotNull(okResult.Value);

        // 2. Test MarkAsRead
        var readResult = await controller.MarkAsRead(notifId);
        Assert.IsType<NoContentResult>(readResult);

        mockQueryService.Verify(s => s.MarkAsReadAsync(_tenantId, _userId1, notifId), Times.Once);
    }
}
