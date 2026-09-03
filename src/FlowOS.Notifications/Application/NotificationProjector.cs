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
        string? message = null;
        string severity = "Info";
        Guid? targetUserId = null;

        // Try direct metadata first
        if (ev.Metadata.TryGetValue("Message", out var msgObj) && msgObj is string msg && !string.IsNullOrWhiteSpace(msg))
        {
            message = msg;
            if (ev.Metadata.TryGetValue("Severity", out var sevObj) && sevObj is string sev)
            {
                severity = sev;
            }
        }

        if (ev.Metadata.TryGetValue("TargetUserId", out var userObj) && userObj is string userStr && Guid.TryParse(userStr, out var userId))
        {
            targetUserId = userId;
        }
        else if (ev.Metadata.TryGetValue("AssignedTo", out var assignObj) && assignObj is string assignStr && Guid.TryParse(assignStr, out var assignedId))
        {
            targetUserId = assignedId;
        }

        // If not found, inspect Payload JSON in Metadata
        if (ev.Metadata.TryGetValue("Payload", out var payloadJson) && !string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;
                if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (message == null && root.TryGetProperty("Message", out var msgProp) && msgProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        message = msgProp.GetString();
                    }

                    if (root.TryGetProperty("Severity", out var sevProp) && sevProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        severity = sevProp.GetString() ?? severity;
                    }

                    if (!targetUserId.HasValue)
                    {
                        if (root.TryGetProperty("TargetUserId", out var targetProp) && Guid.TryParse(targetProp.GetString(), out var tid))
                        {
                            targetUserId = tid;
                        }
                        else if (root.TryGetProperty("AssignedTo", out var assignProp) && Guid.TryParse(assignProp.GetString(), out var aid))
                        {
                            targetUserId = aid;
                        }
                    }
                }
            }
            catch
            {
                // Ignore malformed JSON in Payload
            }
        }

        // Fallback default message if still null
        if (string.IsNullOrWhiteSpace(message))
        {
            if (ev.EventType == "WorkflowStarted" || ev.EventType == "WorkflowCompleted")
            {
                // Internal lifecycle events should not produce general broadcast user inbox notifications
                return null;
            }

            (message, var defaultSeverity) = ev.EventType switch
            {
                "EVT-WORKFLOW-STARTED" => ("Workflow started", "Info"),
                "EVT-TASK-ASSIGNED" => ("Task assigned to you", "Info"),
                "EVT-TASK-OVERDUE" => ("Task overdue", "Warning"),
                "EVT-WORKFLOW-STUCK" => ("Workflow needs attention", "Critical"),
                "EVT-AGENT-INSIGHT" => ("New agent insight available", "Info"),
                "EVT-ESCALATE" => ("Task SLA breached — Workflow automatically escalated", "High"),
                "EVT-TASK-TIMEOUT" => ("Task SLA timeout — Triggered escalation", "High"),
                var t when t.Contains("ESCALAT", StringComparison.OrdinalIgnoreCase) => ($"Task Escalated: {t}", "High"),
                var t when t.Contains("TIMEOUT", StringComparison.OrdinalIgnoreCase) => ($"Task Timeout: {t}", "High"),
                _ => ($"Event: {ev.EventType}", "Info") 
            };
            if (severity == "Info") severity = defaultSeverity;
        }

        return new Notification(ev.TenantId, ev.EventType, message, severity, ev.CorrelationId, targetUserId);
    }
}

