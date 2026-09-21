using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlowOS.Application.Common.Interfaces;

public sealed record FlowOsHostedLlmPublicSettings(
    bool Enabled,
    bool HasApiKey,
    string Provider,
    string Model,
    string? Endpoint,
    int MaxCompletionsPerDay);

public sealed record FlowOsHostedLlmLease(
    bool Allowed,
    string? ApiKey,
    string? Model,
    string? Endpoint,
    string? Code,
    string? Message,
    Guid? TenantId = null,
    DateOnly? UsageDateUtc = null);

public interface IFlowOsHostedLlmRuntime
{
    bool IsConfigured { get; }

    FlowOsHostedLlmPublicSettings PublicSettings { get; }

    Task<FlowOsHostedLlmLease> TryLeaseAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task FinalizeAsync(
        FlowOsHostedLlmLease lease,
        bool succeeded,
        long inputTokens = 0,
        long outputTokens = 0,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public static class FlowOsHostedLlmCodes
{
    public const string Unavailable = "MCP-HOSTED-LLM-UNAVAILABLE";
    public const string Quota = "MCP-HOSTED-LLM-QUOTA";

    public const string UnavailableMessage =
        "FlowOS hosted OpenAI is not configured. Set FLOWOS_HOSTED_LLM_API_KEY on the FlowOS host.";

    public const string QuotaMessage =
        "This tenant used today's FlowOS hosted OpenAI quota. Wait for reset, raise MaxCompletionsPerDay, or bind a BYO agentProvider.";
}
