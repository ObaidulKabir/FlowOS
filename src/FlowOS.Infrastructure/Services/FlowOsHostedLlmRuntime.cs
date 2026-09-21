using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using Microsoft.Extensions.Configuration;

namespace FlowOS.Infrastructure.Services;

public sealed class FlowOsHostedLlmRuntime : IFlowOsHostedLlmRuntime
{
    private readonly IConfiguration _configuration;
    private readonly ITenantEntitlementService _entitlement;
    private readonly IHostedLlmUsageStore _usageStore;
    private readonly TimeProvider _timeProvider;

    public FlowOsHostedLlmRuntime(
        IConfiguration configuration,
        ITenantEntitlementService entitlement,
        IHostedLlmUsageStore usageStore,
        TimeProvider? timeProvider = null)
    {
        _configuration = configuration;
        _entitlement = entitlement;
        _usageStore = usageStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        var usageDateUtc = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var reservation = await _usageStore.TryReserveAsync(
            tenantId,
            usageDateUtc,
            settings.Model,
            settings.MaxCompletionsPerDay,
            cancellationToken: cancellationToken);
        if (!reservation.Allowed)
        {
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
            null,
            tenantId,
            usageDateUtc);
    }

    public async Task FinalizeAsync(
        FlowOsHostedLlmLease lease,
        bool succeeded,
        long inputTokens = 0,
        long outputTokens = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!lease.Allowed ||
            !lease.TenantId.HasValue ||
            !lease.UsageDateUtc.HasValue ||
            string.IsNullOrWhiteSpace(lease.Model))
        {
            return;
        }

        await _usageStore.FinalizeAsync(
            lease.TenantId.Value,
            lease.UsageDateUtc.Value,
            lease.Model,
            succeeded,
            Math.Max(0, inputTokens),
            Math.Max(0, outputTokens),
            cancellationToken: cancellationToken);
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
