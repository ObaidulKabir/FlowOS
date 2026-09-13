using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Models;
using FlowOS.Events.Models;
using FlowOS.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.BackgroundServices;

public class OutboxProcessorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxProcessorService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(1);

    public OutboxProcessorService(IServiceProvider serviceProvider, ILogger<OutboxProcessorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxProcessorService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error processing outbox messages.");
            }

            try
            {
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("OutboxProcessorService stopped.");
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowOSDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        var now = DateTime.UtcNow;
        var pendingMessages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null && !m.IsDeadLetter && (m.NextRetryUtc == null || m.NextRetryUtc <= now))
            .OrderBy(m => m.OccurredOnUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!pendingMessages.Any()) return;

        foreach (var message in pendingMessages)
        {
            try
            {
                if (message.Type.StartsWith("WorkflowAction:", StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessWorkflowActionMessageAsync(message, scope.ServiceProvider, cancellationToken);
                    message.MarkAsProcessed();
                }
                else
                {
                    DomainEvent? domainEvent = null;
                    try
                    {
                        domainEvent = JsonSerializer.Deserialize<StandardEvent>(message.Payload);
                    }
                    catch
                    {
                        domainEvent = null;
                    }

                    if (domainEvent == null)
                    {
                        domainEvent = new StandardEvent(message.TenantId, message.Type);
                    }

                    await publisher.Publish(new DomainEventNotification<DomainEvent>(domainEvent), cancellationToken);
                    message.MarkAsProcessed();
                }
            }
            catch (Exception ex)
            {
                message.RecordFailure(ex.Message);
                if (message.IsDeadLetter)
                {
                    _logger.LogError(ex, "Outbox message {MessageId} of type {MessageType} reached max retries ({MaxRetries}) and moved to Dead Letter Queue (DLQ).",
                        message.Id, message.Type, message.MaxRetries);
                }
                else
                {
                    _logger.LogWarning(ex, "Failed to process outbox message {MessageId} of type {MessageType}. Retry {RetryCount}/{MaxRetries}, next retry at {NextRetryUtc}",
                        message.Id, message.Type, message.RetryCount, message.MaxRetries, message.NextRetryUtc);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessWorkflowActionMessageAsync(
        OutboxMessage message,
        IServiceProvider sp,
        CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(message.Payload);
        var root = doc.RootElement;
        var actionType = root.TryGetProperty("actionType", out var at) ? at.GetString() : "";

        if (string.Equals(actionType, "Webhook", StringComparison.OrdinalIgnoreCase))
        {
            var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
            var method = root.TryGetProperty("method", out var m) ? m.GetString() : "POST";

            if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var httpMethod = new System.Net.Http.HttpMethod(method ?? "POST");
                using var request = new System.Net.Http.HttpRequestMessage(httpMethod, uri);
                request.Headers.Add("x-tenant-id", message.TenantId.ToString());
                request.Headers.Add("x-flowos-action", "webhook");

                if (root.TryGetProperty("payload", out var payloadElem))
                {
                    request.Content = new System.Net.Http.StringContent(payloadElem.GetRawText(), System.Text.Encoding.UTF8, "application/json");
                }

                var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Webhook to {url} returned HTTP {(int)response.StatusCode}");
                }
            }
        }
        else if (string.Equals(actionType, "Notification", StringComparison.OrdinalIgnoreCase))
        {
            var target = root.TryGetProperty("target", out var tg) ? tg.GetString() : "User";
            var template = root.TryGetProperty("template", out var tm) ? tm.GetString() : "Workflow action triggered";
            var stepId = root.TryGetProperty("stepId", out var sid) ? sid.GetString() : "";

            var dbContext = sp.GetService<FlowOSDbContext>();
            if (dbContext != null)
            {
                var notif = new FlowOS.Notifications.Domain.Notification(
                    message.TenantId,
                    "WorkflowAction",
                    $"[{stepId}] {template} (Target: {target})",
                    "Info",
                    null
                );
                dbContext.Notifications.Add(notif);
                await dbContext.SaveChangesAsync(ct);
            }
        }
        else if (string.Equals(actionType, "PublishEvent", StringComparison.OrdinalIgnoreCase))
        {
            var eventName = root.TryGetProperty("target", out var tg) ? tg.GetString() : null;
            if (!string.IsNullOrWhiteSpace(eventName))
            {
                var publisher = sp.GetRequiredService<IPublisher>();
                var evt = new StandardEvent(message.TenantId, eventName);
                await publisher.Publish(new DomainEventNotification<DomainEvent>(evt), ct);
            }
        }
    }
}
