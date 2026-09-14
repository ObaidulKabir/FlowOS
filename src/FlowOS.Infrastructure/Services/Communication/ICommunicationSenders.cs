using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.Services.Communication;

public record EmailSendRequest(
    Guid TenantId,
    string To,
    string Subject,
    string Body,
    string? HtmlBody = null,
    string? Cc = null,
    string? Bcc = null,
    Dictionary<string, string>? Headers = null);

public record EmailSendResult(
    bool Success,
    string? MessageId = null,
    int? StatusCode = null,
    string? Error = null);

public interface IEmailSender
{
    Task<EmailSendResult> SendEmailAsync(EmailSendRequest request, CancellationToken ct = default);
}

public class DefaultEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DefaultEmailSender> _logger;
    private readonly HttpClient _httpClient;

    public DefaultEmailSender(IConfiguration configuration, ILogger<DefaultEmailSender> logger, HttpClient? httpClient = null)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<EmailSendResult> SendEmailAsync(EmailSendRequest request, CancellationToken ct = default)
    {
        // 1. Webhook endpoint check (e.g. external email gateway / webhook)
        var webhookUrl = _configuration["FlowOS:Communications:Email:WebhookUrl"] 
                         ?? _configuration["FlowOS:Communications:Email:Endpoint"]
                         ?? Environment.GetEnvironmentVariable("EMAIL_WEBHOOK_URL");

        if (!string.IsNullOrWhiteSpace(webhookUrl) && Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri))
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    tenantId = request.TenantId,
                    to = request.To,
                    subject = request.Subject,
                    body = request.Body,
                    htmlBody = request.HtmlBody,
                    cc = request.Cc,
                    bcc = request.Bcc,
                    headers = request.Headers
                });

                using var req = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };

                var res = await _httpClient.SendAsync(req, ct);
                if (res.IsSuccessStatusCode)
                {
                    var msgId = $"webhook_{Guid.NewGuid():N}";
                    _logger.LogInformation("[EmailSender] Email dispatched via Webhook to '{To}' (ID: {Id})", request.To, msgId);
                    return new EmailSendResult(true, msgId, (int)res.StatusCode);
                }

                var errText = await res.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[EmailSender] Email webhook returned HTTP {Status}: {Error}", (int)res.StatusCode, errText);
                return new EmailSendResult(false, null, (int)res.StatusCode, $"Email endpoint error: {errText}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email via webhook endpoint {Url}", webhookUrl);
                return new EmailSendResult(false, null, null, ex.Message);
            }
        }

        // 2. SMTP Delivery check
        var smtpHost = _configuration["FlowOS:Communications:Email:Smtp:Host"]
                       ?? Environment.GetEnvironmentVariable("SMTP_HOST");
        var smtpUser = _configuration["FlowOS:Communications:Email:Smtp:Username"]
                       ?? Environment.GetEnvironmentVariable("SMTP_USER")
                       ?? Environment.GetEnvironmentVariable("SMTP_USERNAME");
        var smtpPass = _configuration["FlowOS:Communications:Email:Smtp:Password"]
                       ?? Environment.GetEnvironmentVariable("SMTP_PASSWORD")
                       ?? Environment.GetEnvironmentVariable("SMTP_PASS");

        if (!string.IsNullOrWhiteSpace(smtpHost) && !string.IsNullOrWhiteSpace(smtpPass))
        {
            var smtpPortStr = _configuration["FlowOS:Communications:Email:Smtp:Port"]
                              ?? Environment.GetEnvironmentVariable("SMTP_PORT")
                              ?? "587";
            if (!int.TryParse(smtpPortStr, out var smtpPort) || smtpPort <= 0)
                smtpPort = 587;

            var enableSslStr = _configuration["FlowOS:Communications:Email:Smtp:EnableSsl"]
                               ?? Environment.GetEnvironmentVariable("SMTP_ENABLE_SSL")
                               ?? "true";
            var enableSsl = !string.Equals(enableSslStr, "false", StringComparison.OrdinalIgnoreCase);

            var fromEmail = _configuration["FlowOS:Communications:Email:OfficialEmail"]
                            ?? Environment.GetEnvironmentVariable("SMTP_FROM")
                            ?? "admin@flowosbd.com";
            var fromName = _configuration["FlowOS:Communications:Email:SenderName"]
                           ?? "FlowOS Admin";

            try
            {
                using var mailMsg = new MailMessage();
                mailMsg.From = new MailAddress(fromEmail, fromName);
                mailMsg.To.Add(request.To);
                mailMsg.Subject = request.Subject;
                mailMsg.Body = request.HtmlBody ?? request.Body;
                mailMsg.IsBodyHtml = !string.IsNullOrWhiteSpace(request.HtmlBody);

                if (!string.IsNullOrWhiteSpace(request.HtmlBody) && !string.IsNullOrWhiteSpace(request.Body))
                {
                    var plainTextView = AlternateView.CreateAlternateViewFromString(request.Body, Encoding.UTF8, "text/plain");
                    var htmlView = AlternateView.CreateAlternateViewFromString(request.HtmlBody, Encoding.UTF8, "text/html");
                    mailMsg.AlternateViews.Add(plainTextView);
                    mailMsg.AlternateViews.Add(htmlView);
                }

                if (!string.IsNullOrWhiteSpace(request.Cc))
                    mailMsg.CC.Add(request.Cc);
                if (!string.IsNullOrWhiteSpace(request.Bcc))
                    mailMsg.Bcc.Add(request.Bcc);

                if (request.Headers != null)
                {
                    foreach (var (k, v) in request.Headers)
                    {
                        if (string.Equals(k, "Reply-To", StringComparison.OrdinalIgnoreCase))
                        {
                            mailMsg.ReplyToList.Add(v);
                        }
                        else if (!string.Equals(k, "From", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(k, "Sender", StringComparison.OrdinalIgnoreCase))
                        {
                            mailMsg.Headers[k] = v;
                        }
                    }
                }

                using var smtpClient = new SmtpClient(smtpHost, smtpPort)
                {
                    EnableSsl = enableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 15000
                };

                if (!string.IsNullOrWhiteSpace(smtpUser) && !string.IsNullOrWhiteSpace(smtpPass))
                {
                    smtpClient.Credentials = new NetworkCredential(smtpUser, smtpPass);
                }

                await smtpClient.SendMailAsync(mailMsg, ct);

                var msgId = $"smtp_{Guid.NewGuid():N}";
                _logger.LogInformation("[EmailSender] Email sent via SMTP ({Host}:{Port}) to '{To}' (ID: {Id})",
                    smtpHost, smtpPort, request.To, msgId);

                return new EmailSendResult(true, msgId, 200);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EmailSender] Failed to send email via SMTP host {Host}:{Port} to {To}",
                    smtpHost, smtpPort, request.To);
                return new EmailSendResult(false, null, 500, $"SMTP send error: {ex.Message}");
            }
        }

        // 3. Fallback: Log simulation and return success
        var generatedId = $"email_sim_{Guid.NewGuid():N}";
        _logger.LogWarning("[EmailSender] No SMTP credentials configured (set SMTP_HOST, SMTP_USER, SMTP_PASSWORD in environment or appsettings). Simulated email to '{To}' with subject '{Subject}' (ID: {Id})",
            request.To, request.Subject, generatedId);

        return await Task.FromResult(new EmailSendResult(true, generatedId, 200));
    }
}

public record SlackSendRequest(
    Guid TenantId,
    string Channel,
    string Text,
    string? WebhookUrl = null,
    object? Blocks = null,
    object? Attachments = null);

public record SlackSendResult(
    bool Success,
    string? MessageTs = null,
    int? StatusCode = null,
    string? Error = null);

public interface ISlackSender
{
    Task<SlackSendResult> SendSlackMessageAsync(SlackSendRequest request, CancellationToken ct = default);
}

public class DefaultSlackSender : ISlackSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DefaultSlackSender> _logger;
    private readonly HttpClient _httpClient;

    public DefaultSlackSender(IConfiguration configuration, ILogger<DefaultSlackSender> logger, HttpClient? httpClient = null)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<SlackSendResult> SendSlackMessageAsync(SlackSendRequest request, CancellationToken ct = default)
    {
        var targetUrl = !string.IsNullOrWhiteSpace(request.WebhookUrl)
            ? request.WebhookUrl
            : (_configuration[$"FlowOS:Communications:Slack:WebhookUrl"] 
               ?? _configuration[$"FlowOS:Communications:Slack:Endpoint"]);

        if (!string.IsNullOrWhiteSpace(targetUrl) && Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
        {
            try
            {
                var payloadObj = new Dictionary<string, object>
                {
                    ["channel"] = request.Channel,
                    ["text"] = request.Text
                };

                if (request.Blocks != null) payloadObj["blocks"] = request.Blocks;
                if (request.Attachments != null) payloadObj["attachments"] = request.Attachments;

                var payload = JsonSerializer.Serialize(payloadObj);

                using var req = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };

                var res = await _httpClient.SendAsync(req, ct);
                if (res.IsSuccessStatusCode)
                {
                    return new SlackSendResult(true, $"slack_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", (int)res.StatusCode);
                }

                var errText = await res.Content.ReadAsStringAsync(ct);
                return new SlackSendResult(false, null, (int)res.StatusCode, $"Slack endpoint error: {errText}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Slack message to {Url}", targetUrl);
                return new SlackSendResult(false, null, null, ex.Message);
            }
        }

        // Default local / test mode: log message dispatch and return success
        var generatedTs = $"slack_sim_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        _logger.LogInformation("[SlackSender] Simulated Slack message dispatched to '{Channel}': {Text}",
            request.Channel, request.Text);

        return await Task.FromResult(new SlackSendResult(true, generatedTs, 200));
    }
}

public record WhatsAppSendRequest(
    Guid TenantId,
    string Recipient,
    string? TemplateName = null,
    string? Message = null,
    string Language = "en_US",
    object? Parameters = null,
    string? MediaUrl = null);

public record WhatsAppSendResult(
    bool Success,
    string? MessageSid = null,
    int? StatusCode = null,
    string? Error = null);

public interface IWhatsAppSender
{
    Task<WhatsAppSendResult> SendWhatsAppMessageAsync(WhatsAppSendRequest request, CancellationToken ct = default);
}

public class DefaultWhatsAppSender : IWhatsAppSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DefaultWhatsAppSender> _logger;
    private readonly HttpClient _httpClient;

    public DefaultWhatsAppSender(IConfiguration configuration, ILogger<DefaultWhatsAppSender> logger, HttpClient? httpClient = null)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<WhatsAppSendResult> SendWhatsAppMessageAsync(WhatsAppSendRequest request, CancellationToken ct = default)
    {
        var targetUrl = _configuration[$"FlowOS:Communications:WhatsApp:WebhookUrl"] 
                        ?? _configuration[$"FlowOS:Communications:WhatsApp:Endpoint"];

        if (!string.IsNullOrWhiteSpace(targetUrl) && Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
        {
            try
            {
                var payloadObj = new Dictionary<string, object>
                {
                    ["tenantId"] = request.TenantId,
                    ["recipient"] = request.Recipient,
                    ["language"] = request.Language
                };

                if (!string.IsNullOrEmpty(request.TemplateName)) payloadObj["templateName"] = request.TemplateName;
                if (!string.IsNullOrEmpty(request.Message)) payloadObj["message"] = request.Message;
                if (request.Parameters != null) payloadObj["parameters"] = request.Parameters;
                if (!string.IsNullOrEmpty(request.MediaUrl)) payloadObj["mediaUrl"] = request.MediaUrl;

                var payload = JsonSerializer.Serialize(payloadObj);

                using var req = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };

                var res = await _httpClient.SendAsync(req, ct);
                if (res.IsSuccessStatusCode)
                {
                    return new WhatsAppSendResult(true, $"wa_{Guid.NewGuid():N}", (int)res.StatusCode);
                }

                var errText = await res.Content.ReadAsStringAsync(ct);
                return new WhatsAppSendResult(false, null, (int)res.StatusCode, $"WhatsApp endpoint error: {errText}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send WhatsApp message to {Url}", targetUrl);
                return new WhatsAppSendResult(false, null, null, ex.Message);
            }
        }

        // Default local / test mode: log message dispatch and return success
        var generatedSid = $"wa_sim_{Guid.NewGuid():N}";
        _logger.LogInformation("[WhatsAppSender] Simulated WhatsApp message dispatched to '{Recipient}' (Template: {Template}): {Message}",
            request.Recipient, request.TemplateName ?? "N/A", request.Message ?? "(template parameters)");

        return await Task.FromResult(new WhatsAppSendResult(true, generatedSid, 200));
    }
}
