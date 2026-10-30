using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Billing;
using FlowOS.Infrastructure.Services.Communication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlowOS.Infrastructure.Services;

public sealed class EmailPlanRequestNotifier : IPlanRequestNotifier
{
    /// <summary>Address that receives a notice for every new plan request. Unset means no admin notice.</summary>
    public const string AdminEmailSetting = "FLOWOS_PLAN_REQUEST_NOTIFY_EMAIL";

    private readonly IEmailSender _email;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailPlanRequestNotifier> _logger;

    public EmailPlanRequestNotifier(
        IEmailSender email,
        IConfiguration configuration,
        ILogger<EmailPlanRequestNotifier> logger)
    {
        _email = email;
        _configuration = configuration;
        _logger = logger;
    }

    public Task NotifyAdminOfRequestAsync(
        Guid tenantId, string tenantName, PlanRequestDto request, CancellationToken cancellationToken = default)
    {
        var to = _configuration[AdminEmailSetting]?.Trim();
        if (string.IsNullOrEmpty(to))
            return Task.CompletedTask;

        var body = new StringBuilder()
            .AppendLine($"Tenant {tenantName} ({tenantId}) asked to activate the {request.RequestedPlan} plan.")
            .AppendLine($"Payment reference: {request.PaymentReference ?? "(none)"}")
            .AppendLine($"Billing contact: {request.BillingContactEmail ?? "(none)"}")
            .AppendLine($"Notes: {request.Notes ?? "(none)"}")
            .AppendLine()
            .AppendLine("Review it in the FlowOS admin dashboard under Tenants > Plan requests.")
            .ToString();

        return SendAsync(tenantId, to, $"FlowOS plan request: {tenantName} wants {request.RequestedPlan}", body, cancellationToken);
    }

    public Task NotifyTenantOfDecisionAsync(
        Guid tenantId, string tenantName, PlanRequestDto request, CancellationToken cancellationToken = default)
    {
        var to = request.BillingContactEmail?.Trim();
        if (string.IsNullOrEmpty(to))
            return Task.CompletedTask;

        var approved = string.Equals(request.Status, "Approved", StringComparison.OrdinalIgnoreCase);
        var body = new StringBuilder()
            .AppendLine(approved
                ? $"Your {request.RequestedPlan} plan for {tenantName} is now active. Runtime tools are available."
                : $"Your {request.RequestedPlan} plan request for {tenantName} was not approved.")
            .AppendLine(string.IsNullOrWhiteSpace(request.AdminNotes) ? string.Empty : $"Note from the administrator: {request.AdminNotes}")
            .AppendLine(approved ? string.Empty : "You can fix the reason and call request_plan_activation again.")
            .ToString();

        return SendAsync(
            tenantId, to,
            approved ? $"FlowOS plan activated: {request.RequestedPlan}" : "FlowOS plan request not approved",
            body, cancellationToken);
    }

    private async Task SendAsync(Guid tenantId, string to, string subject, string body, CancellationToken ct)
    {
        try
        {
            var result = await _email.SendEmailAsync(new EmailSendRequest(tenantId, to, subject, body), ct);
            if (!result.Success)
                _logger.LogWarning("Plan request e-mail to {Recipient} failed: {Error}", to, result.Error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Plan request e-mail to {Recipient} failed.", to);
        }
    }
}
