using System;
using System.Threading.Tasks;
using FlowOS.Application.Billing;
using FlowOS.MCP.Models;
using FlowOS.MCP.Services;
using Newtonsoft.Json.Linq;

namespace FlowOS.MCP.Tools;

/// <summary>
/// Lets a tenant that is blocked by MCP-PLAN-REQUIRED see its plan state and ask a platform
/// administrator to activate a paid plan. It never activates a plan itself.
/// </summary>
public class PlanActivationMcpTools
{
    internal const int PaymentReferenceMaxLength = 200;
    internal const int EmailMaxLength = 254;
    internal const int NotesMaxLength = 1000;

    private readonly ITenantPlanActivationService _activation;

    public PlanActivationMcpTools(ITenantPlanActivationService activation)
    {
        _activation = activation;
    }

    public async Task<CallToolResult> GetPlanStatus(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);
            var status = await _activation.GetStatusAsync(tenantId);
            if (status == null)
                return McpToolResults.Fail("MCP-TENANT-002", "Tenant not found.");

            return McpToolResults.Success(new
            {
                plan = status.Plan,
                billingStatus = status.BillingStatus,
                canRunRuntime = status.CanRunRuntime,
                billingEnforced = status.BillingEnforced,
                planRequest = Describe(status.PlanRequest),
                nextStep = NextStep(status)
            });
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to read plan status.");
        }
    }

    public async Task<CallToolResult> RequestPlanActivation(JObject args)
    {
        try
        {
            var tenantId = McpTenantResolver.ResolveRequired(args);

            var plan = args["plan"]?.ToString();
            if (string.IsNullOrWhiteSpace(plan))
                return McpToolResults.Fail("MCP-ARG-002", "plan is required: Starter, Builder, Team, Growth, Scale, or Enterprise.");

            var reference = Optional(args, "paymentReference", PaymentReferenceMaxLength, out var error);
            if (error != null) return McpToolResults.Fail("MCP-ARG-002", error);
            var email = Optional(args, "billingContactEmail", EmailMaxLength, out error);
            if (error != null) return McpToolResults.Fail("MCP-ARG-002", error);
            var notes = Optional(args, "notes", NotesMaxLength, out error);
            if (error != null) return McpToolResults.Fail("MCP-ARG-002", error);

            var result = await _activation.SubmitAsync(
                tenantId,
                new PlanActivationInput(plan, reference, email, notes),
                idempotent: true);

            return result.Outcome switch
            {
                PlanActivationOutcome.TenantNotFound =>
                    McpToolResults.Fail("MCP-TENANT-002", "Tenant not found."),
                PlanActivationOutcome.InvalidPlan =>
                    McpToolResults.Fail("MCP-ARG-002", "plan must be Starter, Builder, Team, Growth, Scale, or Enterprise."),
                PlanActivationOutcome.AlreadyActive => McpToolResults.Success(new
                {
                    outcome = "AlreadyActive",
                    message = "This tenant already has an active paid plan. No request was filed."
                }),
                PlanActivationOutcome.AlreadyPending => McpToolResults.Success(new
                {
                    outcome = "AlreadyPending",
                    message = "An identical request is already waiting for a platform administrator. No new request was filed.",
                    planRequest = Describe(result.PlanRequest)
                }),
                _ => McpToolResults.Success(new
                {
                    outcome = "Submitted",
                    message = "Request filed. A platform administrator will review it; until then runtime tools stay blocked. Call get_plan_status to follow it.",
                    planRequest = Describe(result.PlanRequest)
                })
            };
        }
        catch (McpToolException ex)
        {
            return McpToolResults.Fail(ex.Code, ex.Message);
        }
        catch (Exception)
        {
            return McpToolResults.Fail("MCP-INTERNAL", "Failed to file the plan activation request.");
        }
    }

    private static string? Optional(JObject args, string name, int maxLength, out string? error)
    {
        error = null;
        var value = args[name]?.ToString()?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        if (value.Length > maxLength)
            error = $"{name} must be at most {maxLength} characters.";
        return value;
    }

    private static object? Describe(PlanRequestDto? request)
    {
        if (request == null)
            return null;

        return new
        {
            requestedPlan = request.RequestedPlan,
            status = request.Status,
            paymentReference = request.PaymentReference,
            requestedAt = request.RequestedAt,
            reviewedAt = request.ReviewedAt,
            rejectionReason = string.Equals(request.Status, "Rejected", StringComparison.OrdinalIgnoreCase)
                ? request.AdminNotes
                : null
        };
    }

    private static string NextStep(TenantPlanStatus status)
    {
        if (status.CanRunRuntime)
            return "Your plan is active. Runtime tools are available.";
        if (!status.BillingEnforced)
            return "Billing is not enforced in this environment; runtime tools are available.";

        var request = status.PlanRequest;
        if (request != null && string.Equals(request.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            return "Your request is waiting for a platform administrator. Nothing more is needed.";
        if (request != null && string.Equals(request.Status, "Rejected", StringComparison.OrdinalIgnoreCase))
            return "Your last request was rejected. Fix the reason, then call request_plan_activation again.";
        return "Call request_plan_activation with your plan and payment reference.";
    }
}
