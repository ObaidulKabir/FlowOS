using System;
using System.Threading;
using System.Threading.Tasks;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FlowOS.Infrastructure.Services;

public sealed class TenantEntitlementService : ITenantEntitlementService
{
    private readonly FlowOSDbContext _db;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public TenantEntitlementService(
        FlowOSDbContext db,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        _db = db;
        _environment = environment;
        _configuration = configuration;
    }

    public bool IsEnforced
    {
        get
        {
            var configured = _configuration["FLOWOS_BILLING_ENFORCE"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (bool.TryParse(configured, out var flag))
                    return flag;
                if (string.Equals(configured, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(configured, "yes", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(configured, "on", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(configured, "0", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(configured, "no", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(configured, "off", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            var mcpKey = _configuration["MCP_API_KEY"];
            if (string.Equals(mcpKey, "disabled", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mcpKey, "none", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !_environment.IsDevelopment();
        }
    }

    public bool McpToolRequiresPaidPlan(string toolName, bool mutating, string sideEffect) =>
        TenantEntitlementPolicy.McpToolRequiresPaidPlan(toolName, mutating, sideEffect);

    public async Task<TenantEntitlementDecision> EnsureRuntimeAllowedAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnforced)
            return new TenantEntitlementDecision(true);

        if (tenantId == Guid.Empty)
            return Denied();

        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, cancellationToken);
        if (tenant == null)
            return Denied();

        if (tenant.CanRunRuntime)
            return new TenantEntitlementDecision(true);

        return Denied();
    }

    private static TenantEntitlementDecision Denied() =>
        new(false, TenantEntitlementPolicy.PlanRequiredCode, TenantEntitlementPolicy.PlanRequiredMessage);
}
