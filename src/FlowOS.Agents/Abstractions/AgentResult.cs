using System.Collections.Generic;

namespace FlowOS.Agents.Abstractions;

public static class AgentFailureCodes
{
    public const string EntitlementDenied = "ENTITLEMENT_DENIED";
    public const string HostedQuotaDenied = "HOSTED_QUOTA_DENIED";
    public const string InvalidModelOutput = "INVALID_MODEL_OUTPUT";
    // DX report F12: a model reply whose "insight" exceeds the 2000-character cap used to fail with
    // the generic InvalidModelOutput code, distinguishable from any other malformed reply (bad JSON,
    // missing fields, wrong types) only by re-parsing FailureReason's free text. FieldOverflow is a
    // typed, additive failure code for this one diagnosable case specifically - see AgentResult's
    // FailureField/FailureLimit/FailureActualLength, set only when this code is used.
    public const string FieldOverflow = "FIELD_OVERFLOW";
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

    // DX report F12: structured context for FailureCode == AgentFailureCodes.FieldOverflow only -
    // the field name that overflowed (e.g. "insight"), its character limit, and the actual length
    // the model emitted. Null for every other failure code.
    public string? FailureField { get; set; }
    public int? FailureLimit { get; set; }
    public int? FailureActualLength { get; set; }

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
