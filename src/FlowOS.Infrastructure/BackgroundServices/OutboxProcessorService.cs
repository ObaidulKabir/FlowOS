using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Models;
using FlowOS.Domain.Entities;
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
        var actionType = root.TryGetProperty("actionType", out var at) ? at.GetString() ?? "" : "";
        var stepId = root.TryGetProperty("stepId", out var sid) ? sid.GetString() ?? "" : "";
        var triggerPhase = root.TryGetProperty("triggerPhase", out var tp) ? tp.GetString() ?? "" : "";
        var capability = root.TryGetProperty("capability", out var cap) ? cap.GetString() : null;
        var target = root.TryGetProperty("url", out var u) ? u.GetString() : (root.TryGetProperty("target", out var tg) ? tg.GetString() : null);
        if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(capability))
        {
            target = capability;
        }

        var workflowInstanceId = Guid.Empty;
        if (root.TryGetProperty("workflowInstanceId", out var widElem) && widElem.TryGetGuid(out var parsedWid))
        {
            workflowInstanceId = parsedWid;
        }

        string? requestPayloadSnippet = null;
        if (root.TryGetProperty("payload", out var plElem))
        {
            requestPayloadSnippet = plElem.GetRawText();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int? httpStatusCode = null;
        string? responseSnippet = null;
        string? errorMessage = null;
        string status = "Succeeded";

        try
        {
            if (string.Equals(actionType, "Webhook", StringComparison.OrdinalIgnoreCase))
            {
                var url = root.TryGetProperty("url", out var urlElem) ? urlElem.GetString() : null;
                var method = root.TryGetProperty("method", out var m) ? m.GetString() : "POST";

                if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    var httpMethod = new System.Net.Http.HttpMethod(method ?? "POST");
                    using var request = new System.Net.Http.HttpRequestMessage(httpMethod, uri);

                    var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    request.Headers.Add("x-tenant-id", message.TenantId.ToString());
                    request.Headers.Add("x-flowos-action", "webhook");
                    request.Headers.Add("X-FlowOS-Timestamp", nowSeconds.ToString());
                    request.Headers.Add("X-FlowOS-Delivery", message.Id.ToString());

                    var rawPayload = requestPayloadSnippet ?? "{}";
                    request.Content = new System.Net.Http.StringContent(rawPayload, System.Text.Encoding.UTF8, "application/json");

                    // Attach custom headers if provided
                    if (root.TryGetProperty("headers", out var headersElem) && headersElem.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in headersElem.EnumerateObject())
                        {
                            var val = prop.Value.GetString();
                            if (val != null)
                            {
                                if (!request.Headers.TryAddWithoutValidation(prop.Name, val) && request.Content != null)
                                {
                                    request.Content.Headers.TryAddWithoutValidation(prop.Name, val);
                                }
                            }
                        }
                    }

                    // Compute HMAC-SHA256 signature if enabled
                    var signPayload = !root.TryGetProperty("signPayload", out var spElem) || spElem.GetBoolean();
                    if (signPayload)
                    {
                        var sigService = sp.GetService<FlowOS.Core.Common.Interfaces.IWebhookSignatureService>();
                        if (sigService != null)
                        {
                            var db = sp.GetService<FlowOSDbContext>();
                            var tenant = db != null ? await db.Tenants.FindAsync(new object[] { message.TenantId }, ct) : null;
                            var signingSecret = tenant?.WebhookSigningSecret;
                            if (string.IsNullOrWhiteSpace(signingSecret))
                            {
                                signingSecret = $"whsec_{message.TenantId.ToString("N")}";
                            }

                            var sigHeader = sigService.FormatSignatureHeader(signingSecret, rawPayload, nowSeconds);
                            request.Headers.Add("X-FlowOS-Signature", sigHeader);
                        }
                    }

                    var response = await client.SendAsync(request, ct);
                    httpStatusCode = (int)response.StatusCode;
                    sw.Stop();

                    try
                    {
                        responseSnippet = await response.Content.ReadAsStringAsync(ct);
                    }
                    catch
                    {
                        responseSnippet = null;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        status = "Failed";
                        errorMessage = $"Webhook to {url} returned HTTP {httpStatusCode}";
                        throw new InvalidOperationException(errorMessage);
                    }
                }
            }
            else if (string.Equals(actionType, "Notification", StringComparison.OrdinalIgnoreCase))
            {
                var template = root.TryGetProperty("template", out var tm) ? tm.GetString() : "Workflow action triggered";

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

                sw.Stop();
                responseSnippet = "Notification dispatched successfully";
            }
            else if (string.Equals(actionType, "PublishEvent", StringComparison.OrdinalIgnoreCase))
            {
                var eventName = target;
                if (!string.IsNullOrWhiteSpace(eventName))
                {
                    var publisher = sp.GetRequiredService<IPublisher>();
                    var evt = new StandardEvent(message.TenantId, eventName);
                    await publisher.Publish(new DomainEventNotification<DomainEvent>(evt), ct);
                }

                sw.Stop();
                responseSnippet = $"Event '{eventName}' published successfully";
            }
            else if (string.Equals(actionType, "InvokeCapability", StringComparison.OrdinalIgnoreCase))
            {
                var configuration = sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
                var remoteInvokeEnabled = bool.TryParse(
                    configuration?["FlowOS:Capabilities:EnableRemoteInvoke"],
                    out var enabledFlag) && enabledFlag;
                if (!remoteInvokeEnabled)
                {
                    throw new InvalidOperationException("Remote capability invocation is disabled. Set FlowOS:Capabilities:EnableRemoteInvoke=true to enable InvokeCapability actions.");
                }

                var capabilityName = !string.IsNullOrWhiteSpace(capability) ? capability : target;
                if (string.IsNullOrWhiteSpace(capabilityName))
                {
                    throw new InvalidOperationException("InvokeCapability action requires 'capability' (or fallback target) in payload.");
                }

                var registry = sp.GetService<ICapabilityRegistryService>();
                if (registry == null)
                {
                    throw new InvalidOperationException("Capability registry service is unavailable.");
                }

                var bindingCheck = await registry.ValidateBindingAsync(message.TenantId, capabilityName, ct);
                if (!bindingCheck.IsValid || bindingCheck.Binding == null)
                {
                    throw new InvalidOperationException($"Capability binding invalid for '{capabilityName}': {bindingCheck.Message}");
                }

                var binding = bindingCheck.Binding;
                var payloadJson = requestPayloadSnippet ?? "{}";
                object? parsedPayload = null;
                try
                {
                    parsedPayload = JsonSerializer.Deserialize<object>(payloadJson);
                }
                catch
                {
                    parsedPayload = payloadJson;
                }

                var invocation = new
                {
                    capability = capabilityName,
                    tenantId = message.TenantId,
                    workflowInstanceId,
                    stepId,
                    triggerPhase,
                    correlationId = message.Id,
                    idempotencyKey = $"{message.TenantId:N}:{workflowInstanceId:N}:{stepId}:{message.Id:N}",
                    payload = parsedPayload
                };

                var client = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromMilliseconds(Math.Clamp(binding.TimeoutMs, 1000, 120000))
                };
                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, binding.EndpointUrl);
                request.Headers.Add("x-tenant-id", message.TenantId.ToString());
                request.Headers.Add("x-flowos-capability", capabilityName);
                request.Headers.Add("x-flowos-delivery", message.Id.ToString());
                if (!string.IsNullOrWhiteSpace(binding.AuthRef))
                {
                    request.Headers.Add("x-flowos-auth-ref", binding.AuthRef);
                }

                var invocationJson = JsonSerializer.Serialize(invocation);
                request.Content = new System.Net.Http.StringContent(invocationJson, System.Text.Encoding.UTF8, "application/json");

                var response = await client.SendAsync(request, ct);
                httpStatusCode = (int)response.StatusCode;
                responseSnippet = await response.Content.ReadAsStringAsync(ct);
                sw.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    status = "Failed";
                    errorMessage = $"Capability '{capabilityName}' returned HTTP {httpStatusCode}.";
                    throw new InvalidOperationException(errorMessage);
                }

                if (!string.IsNullOrWhiteSpace(responseSnippet))
                {
                    try
                    {
                        using var responseDoc = JsonDocument.Parse(responseSnippet);
                        if (responseDoc.RootElement.TryGetProperty("status", out var statusElem))
                        {
                            var remoteStatus = statusElem.GetString();
                            if (string.Equals(remoteStatus, "Failed", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(remoteStatus, "RetryableFailure", StringComparison.OrdinalIgnoreCase))
                            {
                                status = "Failed";
                                errorMessage = $"Capability '{capabilityName}' returned status '{remoteStatus}'.";
                                throw new InvalidOperationException(errorMessage);
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Non-JSON responses are accepted as success when HTTP status is successful.
                    }
                }
            }
            else
            {
                var configuration = sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
                var rejectUnknownActionTypes = bool.TryParse(
                    configuration?["FlowOS:Actions:RejectUnknownActionTypes"],
                    out var rejectUnknown) && rejectUnknown;

                if (rejectUnknownActionTypes)
                {
                    status = "Failed";
                    errorMessage = $"Unknown workflow action type '{actionType}'.";
                    throw new InvalidOperationException(errorMessage);
                }

                sw.Stop();
                responseSnippet = $"Unknown workflow action type '{actionType}' ignored (compatibility mode).";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            status = "Failed";
            errorMessage ??= ex.Message;
            throw;
        }
        finally
        {
            if (workflowInstanceId != Guid.Empty)
            {
                var db = sp.GetService<FlowOSDbContext>();
                if (db != null)
                {
                    var log = new WorkflowActionExecutionLog(
                        tenantId: message.TenantId,
                        workflowInstanceId: workflowInstanceId,
                        stepId: stepId,
                        triggerPhase: triggerPhase,
                        actionType: actionType,
                        target: target,
                        status: status,
                        durationMs: sw.ElapsedMilliseconds,
                        httpStatusCode: httpStatusCode,
                        requestPayloadSnippet: requestPayloadSnippet,
                        responseSnippet: responseSnippet,
                        errorMessage: errorMessage,
                        attemptNumber: message.RetryCount + 1,
                        outboxMessageId: message.Id
                    );
                    db.ActionExecutionLogs.Add(log);
                }
            }
        }
    }
}
