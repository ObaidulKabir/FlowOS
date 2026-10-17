using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;

namespace FlowOS.Api.Models;

public class SubmitPlanRequest
{
    [Required]
    public string Plan { get; set; } = "Team";
    public string? PaymentReference { get; set; }
    public string? BillingContactEmail { get; set; }
    public string? Notes { get; set; }
}

public class RejectPlanRequest
{
    public string? Reason { get; set; }
}

public class PlanRequestDto
{
    public string RequestedPlan { get; set; } = "Team";
    public string? CommercialTier { get; set; } = "Managed";
    public string? PaymentReference { get; set; }
    public string? BillingContactEmail { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public string? AdminNotes { get; set; }
}

public class TenantPlanRequestItemDto
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string CurrentPlan { get; set; } = string.Empty;
    public string CurrentBillingStatus { get; set; } = string.Empty;
    public bool CanRunRuntime { get; set; }
    public PlanRequestDto PlanRequest { get; set; } = new();
}

public static class CommercialPlans
{
    private static readonly string[] ManagedNames = ["Starter", "Builder", "Team", "Growth", "Scale", "Managed"];

    public static bool TryMap(string? requested, out TenantPlan plan, out string displayName)
    {
        var name = requested?.Trim() ?? string.Empty;
        if (name.Equals("Enterprise", StringComparison.OrdinalIgnoreCase))
        {
            plan = TenantPlan.Enterprise;
            displayName = "Enterprise";
            return true;
        }

        var match = ManagedNames.FirstOrDefault(candidate =>
            candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            plan = TenantPlan.Managed;
            displayName = match;
            return true;
        }

        plan = TenantPlan.None;
        displayName = string.Empty;
        return false;
    }
}

public static class TenantPlanRequestExtensions
{
    public static PlanRequestDto? GetPlanRequest(this Tenant tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant.ConfigurationJson))
            return null;

        try
        {
            var node = JsonNode.Parse(tenant.ConfigurationJson);
            var reqNode = node?["planRequest"];
            if (reqNode == null)
                return null;

            return JsonSerializer.Deserialize<PlanRequestDto>(
                reqNode.ToJsonString(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    public static void SetPlanRequest(this Tenant tenant, PlanRequestDto planRequest)
    {
        JsonObject root;
        try
        {
            root = (!string.IsNullOrWhiteSpace(tenant.ConfigurationJson)
                ? JsonNode.Parse(tenant.ConfigurationJson) as JsonObject
                : null) ?? new JsonObject();
        }
        catch
        {
            root = new JsonObject();
        }

        root["planRequest"] = JsonSerializer.SerializeToNode(planRequest);
        tenant.UpdateConfiguration(root.ToJsonString());
    }
}
