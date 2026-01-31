using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Models; // Changed namespace
using FlowOS.Events.Models;
using FlowOS.Notifications.Domain;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
// using FlowOS.Infrastructure.Persistence; // REMOVED to avoid circular dependency

namespace FlowOS.Notifications.Application;

// Define a minimal interface for what we need from DbContext
public interface INotificationRepository
{
    void Add(Notification notification);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public class NotificationProjector : INotificationHandler<DomainEventNotification<DomainEvent>>
{
    private readonly INotificationRepository _repository; // Use abstraction
    private readonly NotificationStreamService _streamService;
    private readonly ILogger<NotificationProjector> _logger;

    public NotificationProjector(INotificationRepository repository, NotificationStreamService streamService, ILogger<NotificationProjector> logger)
    {
        _repository = repository;
        _streamService = streamService;
        _logger = logger;
    }

    public async Task Handle(DomainEventNotification<DomainEvent> notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;
        var notif = MapEvent(domainEvent);
        
        if (notif != null)
        {
            _repository.Add(notif);
            await _repository.SaveChangesAsync(cancellationToken);
            await _streamService.BroadcastAsync(notif);
            _logger.LogInformation("Projected Notification: {Message}", notif.Message);
        }
    }

    private Notification? MapEvent(DomainEvent ev)
    {
        return ev.EventType switch
        {
            "EVT-WORKFLOW-STARTED" => new Notification(ev.TenantId, ev.EventType, "Workflow started", "Info", ev.CorrelationId),
            "EVT-TASK-ASSIGNED" => new Notification(ev.TenantId, ev.EventType, "Task assigned to you", "Info", ev.CorrelationId),
            "EVT-TASK-OVERDUE" => new Notification(ev.TenantId, ev.EventType, "Task overdue", "Warning", ev.CorrelationId),
            "EVT-WORKFLOW-STUCK" => new Notification(ev.TenantId, ev.EventType, "Workflow needs attention", "Critical", ev.CorrelationId),
            "EVT-AGENT-INSIGHT" => new Notification(ev.TenantId, ev.EventType, "New agent insight available", "Info", ev.CorrelationId),
            _ => new Notification(ev.TenantId, ev.EventType, $"Event: {ev.EventType}", "Info", ev.CorrelationId) 
        };
    }
}

