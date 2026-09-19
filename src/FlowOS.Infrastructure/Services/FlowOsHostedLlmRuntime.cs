using System.Collections.Concurrent;
using FlowOS.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FlowOS.Infrastructure.Services;

public sealed class FlowOsHostedLlmRuntime : IFlowOsHostedLlmRuntime
{
    private static readonly ConcurrentDictionary<string, int> DailyCounts = new(StringComparer.Ordinal);

    private readonly IConfiguration _configuration;
    private readonly ITenantEntitlementService _entitlement;

    public FlowOsHostedLlmRuntime(
        IConfiguration configuration,
        ITenantEntitlementService entitlement)
    {
        _configuration = configuration;
        _entitlement = entitlement;
    }

    public bool IsConfigured => PublicSettings.Enabled && PublicSettings.HasApiKey;

    public FlowOsHostedLlmPublicSettings PublicSettings
    {
        get
        {
            var enabled = ReadEnabled();
            var apiKey = ReadApiKey();
            return new FlowOsHostedLlmPublicSettings(
                enabled,
                !string.IsNullOrWhiteSpace(apiKey),
                "openai",
                ReadModel(),
                ReadEndpoint(),
                ReadMaxCompletionsPerDay());
        }
    }

    public async Task<FlowOsHostedLlmLease> TryLeaseAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var settings = PublicSettings;
        if (!settings.Enabled || !settings.HasApiKey)
        {
            return new FlowOsHostedLlmLease(
                false, null, settings.Model, settings.Endpoint,
                FlowOsHostedLlmCodes.Unavailable,
                FlowOsHostedLlmCodes.UnavailableMessage);
        }

        var plan = await _entitlement.EnsureRuntimeAllowedAsync(tenantId, cancellationToken);
        if (!plan.Allowed)
        {
            return new FlowOsHostedLlmLease(
                false, null, settings.Model, settings.Endpoint,
                plan.Code ?? TenantEntitlementPolicy.PlanRequiredCode,
                plan.Message ?? TenantEntitlementPolicy.PlanRequiredMessage);
        }

        var bucket = $"{tenantId:N}:{DateTime.UtcNow:yyyyMMdd}";
        var used = DailyCounts.AddOrUpdate(bucket, 1, (_, current) => current + 1);
        if (used > settings.MaxCompletionsPerDay)
        {
            DailyCounts.AddOrUpdate(bucket, 0, (_, current) => Math.Max(0, current - 1));
            return new FlowOsHostedLlmLease(
                false, null, settings.Model, settings.Endpoint,
                FlowOsHostedLlmCodes.Quota,
                FlowOsHostedLlmCodes.QuotaMessage);
        }

        return new FlowOsHostedLlmLease(
            true,
            ReadApiKey(),
            settings.Model,
            settings.Endpoint,
            null,
            null);
    }

    private bool ReadEnabled()
    {
        var raw = FirstNonEmpty(_configuration["FlowOS:HostedLlm:Enabled"], _configuration["FLOWOS_HOSTED_LLM_ENABLED"]);
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        return !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(raw, "off", StringComparison.OrdinalIgnoreCase);
    }

    private string? ReadApiKey() =>
        FirstNonEmpty(
            _configuration["FLOWOS_HOSTED_LLM_API_KEY"],
            _configuration["FlowOS:HostedLlm:ApiKey"]);

    private string ReadModel() =>
        FirstNonEmpty(
            _configuration["FlowOS:HostedLlm:Model"],
            _configuration["FLOWOS_HOSTED_LLM_MODEL"])
        ?? "gpt-4o-mini";

    private string? ReadEndpoint() =>
        FirstNonEmpty(
            _configuration["FlowOS:HostedLlm:Endpoint"],
            _configuration["FLOWOS_HOSTED_LLM_ENDPOINT"]);

    private int ReadMaxCompletionsPerDay()
    {
        var raw = FirstNonEmpty(
            _configuration["FlowOS:HostedLlm:MaxCompletionsPerDay"],
            _configuration["FLOWOS_HOSTED_LLM_MAX_PER_DAY"]);
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : 200;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}
