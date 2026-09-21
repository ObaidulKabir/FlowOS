using FlowOS.Domain.Enums;

namespace FlowOS.Application.Common.Interfaces;

public static class AgentObservabilityLimits
{
    public const int DefaultHistoryLimit = 100;
    public const int MaximumHistoryLimit = 200;
    public const int MaximumMetricRecords = 100_000;
    public static readonly TimeSpan DefaultHistoryWindow = TimeSpan.FromDays(30);
    public static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(90);
}

public sealed record AgentExecutionHistoryRequest(
    Guid TenantId,
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? WorkflowInstanceId = null,
    AgentExecutionStatus? Status = null,
    int Limit = AgentObservabilityLimits.DefaultHistoryLimit);

public sealed record AgentEvaluationMetricsRequest(
    Guid TenantId,
    DateTime FromUtc,
    DateTime ToUtc);

public sealed record AgentExecutionHistoryDto(
    DateTime FromUtc,
    DateTime ToUtc,
    int Limit,
    bool HasMore,
    IReadOnlyList<AgentExecutionDto> Executions);

public sealed record AgentExecutionDto(
    Guid ExecutionId,
    Guid? JobId,
    Guid WorkflowInstanceId,
    string StepId,
    string Actor,
    string Mode,
    string? ProviderAlias,
    string? ProviderName,
    string? Model,
    string? PromptAlias,
    string? RuntimeIdentifier,
    string? RuntimeVersion,
    Guid? WorkflowDefinitionId,
    int? WorkflowDefinitionVersion,
    Guid? ContextBindingId,
    Guid? ContextBindingRevisionId,
    long? ContextVersion,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    long? DurationMs,
    string Status,
    bool? Success,
    string? FailureCode,
    int? HttpStatusCode,
    long? InputTokens,
    long? OutputTokens,
    string? SuggestedEvent,
    double? Confidence,
    bool WasCommitted,
    bool WasParked,
    bool WasOverridden,
    string? OverrideActor,
    string? OverrideEvent,
    DateTime? OverriddenAtUtc,
    string? ObservedOutcome,
    bool? OutcomeMatchedSuggestion,
    DateTime? OutcomeEvaluatedAtUtc,
    string OutcomeEvaluation,
    string? OutcomeSource,
    string DecisionOutcome);

public sealed record AgentEvaluationMetricsDto(
    DateTime FromUtc,
    DateTime ToUtc,
    int Runs,
    int SucceededRuns,
    int FailedRuns,
    int CancelledRuns,
    int RunningRuns,
    int Commits,
    int Parks,
    int HostedQuotaDenials,
    int Overrides,
    int Suggestions,
    int EvaluatedOutcomes,
    int UnevaluatedOutcomes,
    int MatchedOutcomes,
    int UnmatchedOutcomes,
    AgentLatencyMetricsDto Latency,
    AgentTokenMetricsDto Tokens,
    AgentConfidenceCalibrationDto ConfidenceCalibration,
    IReadOnlyList<AgentProviderModelMetricsDto> ProviderModelBreakdown);

public sealed record AgentLatencyMetricsDto(
    int Samples,
    double? AverageMs,
    long? P50Ms,
    long? P95Ms);

public sealed record AgentTokenMetricsDto(
    long InputTokens,
    long OutputTokens);

public sealed record AgentConfidenceCalibrationDto(
    int Samples,
    double? BrierScore,
    IReadOnlyList<AgentConfidenceBinDto> Bins);

public sealed record AgentConfidenceBinDto(
    string Label,
    double LowerBound,
    double UpperBound,
    bool UpperBoundInclusive,
    int Samples,
    int MatchedOutcomes,
    double? MeanConfidence,
    double? ObservedMatchRate,
    double? BrierScore);

public sealed record AgentProviderModelMetricsDto(
    string? ProviderAlias,
    string? ProviderName,
    string? Model,
    int Runs,
    int SucceededRuns,
    int FailedRuns,
    int Commits,
    int Parks,
    int HostedQuotaDenials,
    int Overrides,
    int Suggestions,
    int EvaluatedOutcomes,
    int MatchedOutcomes,
    AgentLatencyMetricsDto Latency,
    AgentTokenMetricsDto Tokens);

public interface IAgentObservabilityQueryService
{
    Task<AgentExecutionHistoryDto> GetExecutionHistoryAsync(
        AgentExecutionHistoryRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentEvaluationMetricsDto> GetEvaluationMetricsAsync(
        AgentEvaluationMetricsRequest request,
        CancellationToken cancellationToken = default);
}
