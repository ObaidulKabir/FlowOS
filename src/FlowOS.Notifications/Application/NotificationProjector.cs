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
        string message;
        string severity = "Info";
        Guid? targetUserId = null;

        if (ev.Metadata.TryGetValue("Message", out var msgObj) && msgObj is string msg && !string.IsNullOrWhiteSpace(msg))
        {
            message = msg;
            if (ev.Metadata.TryGetValue("Severity", out var sevObj) && sevObj is string sev)
            {
                severity = sev;
            }
        }
        else
        {
            (message, severity) = ev.EventType switch
            {
                "EVT-WORKFLOW-STARTED" => ("Workflow started", "Info"),
                "EVT-TASK-ASSIGNED" => ("Task assigned to you", "Info"),
                "EVT-TASK-OVERDUE" => ("Task overdue", "Warning"),
                "EVT-WORKFLOW-STUCK" => ("Workflow needs attention", "Critical"),
                "EVT-AGENT-INSIGHT" => ("New agent insight available", "Info"),
                _ => ($"Event: {ev.EventType}", "Info") 
            };
        }

        if (ev.Metadata.TryGetValue("TargetUserId", out var userObj) && userObj is string userStr && Guid.TryParse(userStr, out var userId))
        {
            targetUserId = userId;
        }
        else if (ev.Metadata.TryGetValue("AssignedTo", out var assignObj) && assignObj is string assignStr && Guid.TryParse(assignStr, out var assignedId))
        {
            targetUserId = assignedId;
        }

        return new Notification(ev.TenantId, ev.EventType, message, severity, ev.CorrelationId, targetUserId);
    }
}

