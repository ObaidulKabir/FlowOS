using System.Collections.Generic;

namespace FlowOS.Agents.Abstractions;

public static class AgentFailureCodes
{
    public const string EntitlementDenied = "ENTITLEMENT_DENIED";
    public const string HostedQuotaDenied = "HOSTED_QUOTA_DENIED";
    public const string InvalidModelOutput = "INVALID_MODEL_OUTPUT";
    public const string ProviderAuth = "PROVIDER_AUTH";
    public const string ProviderConfiguration = "PROVIDER_CONFIGURATION";
    public const string ProviderRateLimit = "PROVIDER_RATE_LIMIT";
    public const string ProviderUnavailable = "PROVIDER_UNAVAILABLE";
    public const string ProviderTimeout = "PROVIDER_TIMEOUT";
}

public sealed record AgentTelemetry(
    int? HttpStatusCode = null,
    string? ProviderRequestId = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    long? TotalTokens = null,
    int AttemptCount = 1);

public class AgentResult
{
    public bool Success { get; set; }
    public string? Insight { get; set; }
    public Dictionary<string, object> StructuredData { get; set; } = new();
    public List<SuggestedAction> SuggestedActions { get; set; } = new();
    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public AgentTelemetry? Telemetry { get; set; }

    public static AgentResult FromInsight(string insight, Dictionary<string, object>? data = null)
    {
        return new AgentResult
        {
            Success = true,
            Insight = insight,
            StructuredData = data ?? new Dictionary<string, object>()
        };
    }

    public static AgentResult WithActions(string insight, List<SuggestedAction> actions)
    {
        return new AgentResult
        {
            Success = true,
            Insight = insight,
            SuggestedActions = actions
        };
    }

    public static AgentResult Failure(string reason)
    {
        return new AgentResult
        {
            Success = false,
            FailureReason = reason
        };
    }

    public static AgentResult Failure(
        string code,
        string reason,
        AgentTelemetry? telemetry = null)
    {
        return new AgentResult
        {
            Success = false,
            FailureCode = code,
            FailureReason = reason,
            Telemetry = telemetry
        };
    }
}
