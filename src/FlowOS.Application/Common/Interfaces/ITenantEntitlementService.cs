using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Application.Common.Interfaces;

public record TenantEntitlementDecision(bool Allowed, string? Code = null, string? Message = null);

public interface ITenantEntitlementService
{
    bool IsEnforced { get; }

    bool McpToolRequiresPaidPlan(string toolName, bool mutating, string sideEffect);

    Task<TenantEntitlementDecision> EnsureRuntimeAllowedAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public static class TenantEntitlementPolicy
{
    public const string PlanRequiredCode = "MCP-PLAN-REQUIRED";
    public const string PlanRequiredMessage =
        "Runtime execution requires an active Managed Cloud or Enterprise plan. MCP is included in the tenant subscription.";

    public static bool McpToolRequiresPaidPlan(string toolName, bool mutating, string sideEffect)
    {
        if (IsDesignTimeTool(toolName))
            return false;

        if (!mutating && string.Equals(sideEffect, "none", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool IsDesignTimeTool(string toolName) =>
        toolName.Equals("create_draft_workflowclass", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("update_draft_workflowclass", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("create_context_binding", StringComparison.OrdinalIgnoreCase) ||
        toolName.Equals("update_context_binding", StringComparison.OrdinalIgnoreCase);
}
