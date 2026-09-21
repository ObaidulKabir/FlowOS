using FlowOS.Agents.Abstractions;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;
using FlowOS.Events.Models;

namespace FlowOS.Application.Services;

public sealed class AgentObservabilityQueryService : IAgentObservabilityQueryService
{
    private const string Evaluated = "evaluated";
    private const string Unevaluated = "unevaluated";
    private const string NotApplicable = "not_applicable";
    private const string WorkflowEventSource = "workflow_event";
    private const string ExecutionCommitSource = "execution_commit";
    private const string RecordedOutcomeSource = "recorded_outcome";

    private readonly IAgentExecutionHistoryStore _history;
    private readonly IUnitOfWork _unitOfWork;

    public AgentObservabilityQueryService(
        IAgentExecutionHistoryStore history,
        IUnitOfWork unitOfWork)
    {
        _history = history;
        _unitOfWork = unitOfWork;
    }

    public async Task<AgentExecutionHistoryDto> GetExecutionHistoryAsync(
        AgentExecutionHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTenant(request.TenantId);
        var (fromUtc, toUtc) = ValidateWindow(request.FromUtc, request.ToUtc);
        if (request.Limit is < 1 or > AgentObservabilityLimits.MaximumHistoryLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"Limit must be between 1 and {AgentObservabilityLimits.MaximumHistoryLimit}.");
        }

        var records = await _history.ListAsync(
            new AgentExecutionHistoryQuery(
                request.TenantId,
                request.WorkflowInstanceId,
                fromUtc,
                toUtc,
                request.Status,
                request.Limit + 1),
            cancellationToken);
        var hasMore = records.Count > request.Limit;
        var selected = records.Take(request.Limit).ToList();
        var evaluated = await EvaluateAsync(
            request.TenantId,
            selected,
            cancellationToken);

        return new AgentExecutionHistoryDto(
            fromUtc,
            toUtc,
            request.Limit,
            hasMore,
            evaluated.Select(ToDto).ToList());
    }

    public async Task<AgentEvaluationMetricsDto> GetEvaluationMetricsAsync(
        AgentEvaluationMetricsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTenant(request.TenantId);
        var (fromUtc, toUtc) = ValidateWindow(request.FromUtc, request.ToUtc);

        var records = await _history.ListAsync(
            new AgentExecutionHistoryQuery(
                request.TenantId,
                FromUtc: fromUtc,
                ToUtc: toUtc,
                Limit: AgentObservabilityLimits.MaximumMetricRecords + 1),
            cancellationToken);
        if (records.Count > AgentObservabilityLimits.MaximumMetricRecords)
        {
            throw new InvalidOperationException(
                $"The requested window exceeds {AgentObservabilityLimits.MaximumMetricRecords} executions. Narrow the UTC window.");
        }

        var evaluated = await EvaluateAsync(
            request.TenantId,
            records,
            cancellationToken);

        return BuildMetrics(fromUtc, toUtc, evaluated);
    }

    private async Task<IReadOnlyList<EvaluatedExecution>> EvaluateAsync(
        Guid tenantId,
        IReadOnlyList<AgentExecutionRecord> records,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
            return Array.Empty<EvaluatedExecution>();

        var correlationIds = records
            .SelectMany(record => new[]
            {
                record.CorrelationId ?? Guid.Empty,
                record.WorkflowInstanceId
            })
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        var earliestStart = records.Min(record => record.StartedAtUtc);
        var events = await _unitOfWork.Events.ListForAgentEvaluationAsync(
            tenantId,
            correlationIds,
            earliestStart,
            cancellationToken);
        var byCorrelation = events
            .Where(evt =>
                evt.TenantId == tenantId &&
                evt.CorrelationId.HasValue)
            .GroupBy(evt => evt.CorrelationId!.Value)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<DomainEvent>)group
                    .OrderBy(evt => evt.Timestamp)
                    .ThenBy(evt => evt.EventId)
                    .ToList());

        var nextExecutionById = records
            .GroupBy(record => new
            {
                record.WorkflowInstanceId,
                StepId = record.StepId.ToUpperInvariant()
            })
            .SelectMany(group =>
            {
                var ordered = group
                    .OrderBy(record => record.StartedAtUtc)
                    .ThenBy(record => record.ExecutionId)
                    .ToList();
                return ordered.Select((record, index) => new
                {
                    record.ExecutionId,
                    NextStartedAtUtc = index + 1 < ordered.Count
                        ? ordered[index + 1].StartedAtUtc
                        : (DateTime?)null
                });
            })
            .ToDictionary(item => item.ExecutionId, item => item.NextStartedAtUtc);

        return records
            .Select(record => new EvaluatedExecution(
                record,
                ResolveEvaluation(
                    record,
                    EventsFor(record, byCorrelation),
                    nextExecutionById.GetValueOrDefault(record.ExecutionId))))
            .ToList();
    }

    private static IReadOnlyList<DomainEvent> EventsFor(
        AgentExecutionRecord record,
        IReadOnlyDictionary<Guid, IReadOnlyList<DomainEvent>> byCorrelation)
    {
        var events = new Dictionary<Guid, DomainEvent>();
        if (record.CorrelationId is { } correlationId &&
            byCorrelation.TryGetValue(correlationId, out var correlated))
        {
            foreach (var evt in correlated)
                events[evt.EventId] = evt;
        }
        if (byCorrelation.TryGetValue(record.WorkflowInstanceId, out var instanceEvents))
        {
            foreach (var evt in instanceEvents)
                events[evt.EventId] = evt;
        }

        return events.Values
            .OrderBy(evt => evt.Timestamp)
            .ThenBy(evt => evt.EventId)
            .ToList();
    }

    private static OutcomeEvaluation ResolveEvaluation(
        AgentExecutionRecord record,
        IReadOnlyList<DomainEvent> events,
        DateTime? nextExecutionStartedAtUtc)
    {
        var hasSuggestion = !string.IsNullOrWhiteSpace(record.SuggestedEvent);
        var transition = events.FirstOrDefault(evt =>
            evt.Timestamp >= record.StartedAtUtc &&
            (!nextExecutionStartedAtUtc.HasValue ||
             evt.Timestamp < nextExecutionStartedAtUtc.Value) &&
            TryMetadata(evt, "FromStep", out var fromStep) &&
            string.Equals(fromStep, record.StepId, StringComparison.OrdinalIgnoreCase));

        string? observedOutcome = null;
        bool? matched = null;
        DateTime? evaluatedAtUtc = null;
        string? source = null;
        var derivedOverride = false;
        string? derivedOverrideActor = null;
        string? derivedOverrideEvent = null;
        DateTime? derivedOverriddenAtUtc = null;

        if (transition != null && hasSuggestion)
        {
            observedOutcome = SafeLabel(transition.EventType);
            matched = string.Equals(
                transition.EventType,
                record.SuggestedEvent,
                StringComparison.OrdinalIgnoreCase);
            evaluatedAtUtc = AsUtc(transition.Timestamp);
            source = WorkflowEventSource;
            if (matched == false)
            {
                derivedOverride = true;
                derivedOverrideEvent = observedOutcome;
                derivedOverriddenAtUtc = evaluatedAtUtc;
                if (TryMetadata(transition, "ActorId", out var actor))
                    derivedOverrideActor = SafeLabel(actor);
            }
        }
        else if (record.WasCommitted && hasSuggestion)
        {
            observedOutcome = SafeLabel(record.SuggestedEvent);
            matched = true;
            evaluatedAtUtc = record.EndedAtUtc;
            source = ExecutionCommitSource;
        }
        else if (hasSuggestion && !string.IsNullOrWhiteSpace(record.ObservedOutcome))
        {
            observedOutcome = SafeLabel(record.ObservedOutcome, 500);
            matched = record.OutcomeMatchedSuggestion ??
                string.Equals(
                    record.ObservedOutcome,
                    record.SuggestedEvent,
                    StringComparison.OrdinalIgnoreCase);
            evaluatedAtUtc = record.OutcomeEvaluatedAtUtc;
            source = RecordedOutcomeSource;
        }
        else if (hasSuggestion && !string.IsNullOrWhiteSpace(record.OverrideEvent))
        {
            observedOutcome = SafeLabel(record.OverrideEvent);
            matched = string.Equals(
                record.OverrideEvent,
                record.SuggestedEvent,
                StringComparison.OrdinalIgnoreCase);
            evaluatedAtUtc = record.OverriddenAtUtc;
            source = RecordedOutcomeSource;
        }

        var wasOverridden = record.WasOverridden || derivedOverride;
        return new OutcomeEvaluation(
            hasSuggestion
                ? matched.HasValue ? Evaluated : Unevaluated
                : NotApplicable,
            source,
            observedOutcome,
            matched,
            evaluatedAtUtc,
            wasOverridden,
            SafeLabel(record.OverrideActor) ?? derivedOverrideActor,
            SafeLabel(record.OverrideEvent) ?? derivedOverrideEvent,
            record.OverriddenAtUtc ?? derivedOverriddenAtUtc);
    }

    private static AgentExecutionDto ToDto(EvaluatedExecution evaluated)
    {
        var record = evaluated.Record;
        var outcome = evaluated.Outcome;
        return new AgentExecutionDto(
            record.ExecutionId,
            record.JobId,
            record.WorkflowInstanceId,
            SafeLabel(record.StepId) ?? string.Empty,
            SafeLabel(record.Actor) ?? string.Empty,
            record.Mode.ToString(),
            SafeLabel(record.ProviderAlias),
            SafeLabel(record.ProviderName),
            SafeLabel(record.Model),
            SafeLabel(record.PromptAlias),
            SafeLabel(record.RuntimeIdentifier),
            SafeLabel(record.RuntimeVersion, 100),
            record.WorkflowDefinitionId,
            record.WorkflowDefinitionVersion,
            record.ContextBindingId,
            record.ContextBindingRevisionId,
            record.ContextVersion,
            record.StartedAtUtc,
            record.EndedAtUtc,
            record.DurationMs,
            record.Status.ToString(),
            record.Success,
            SanitizeFailureCode(record.FailureCode),
            record.HttpStatusCode,
            record.InputTokens,
            record.OutputTokens,
            SafeLabel(record.SuggestedEvent),
            record.Confidence,
            record.WasCommitted,
            record.WasParked,
            outcome.WasOverridden,
            outcome.OverrideActor,
            outcome.OverrideEvent,
            outcome.OverriddenAtUtc,
            outcome.ObservedOutcome,
            outcome.MatchedSuggestion,
            outcome.EvaluatedAtUtc,
            outcome.Status,
            outcome.Source,
            record.DecisionOutcome.ToString());
    }

    private static AgentEvaluationMetricsDto BuildMetrics(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyList<EvaluatedExecution> executions)
    {
        var suggestions = executions.Count(HasSuggestion);
        var evaluated = executions.Count(IsEvaluated);
        var matched = executions.Count(item => item.Outcome.MatchedSuggestion == true);
        var quotaDenials = executions.Count(item =>
            SanitizeFailureCode(item.Record.FailureCode) ==
            AgentFailureCodes.HostedQuotaDenied);

        return new AgentEvaluationMetricsDto(
            fromUtc,
            toUtc,
            executions.Count,
            executions.Count(item => item.Record.Status == AgentExecutionStatus.Succeeded),
            executions.Count(item => item.Record.Status == AgentExecutionStatus.Failed),
            executions.Count(item => item.Record.Status == AgentExecutionStatus.Cancelled),
            executions.Count(item => item.Record.Status == AgentExecutionStatus.Running),
            executions.Count(item => item.Record.WasCommitted),
            executions.Count(item => item.Record.WasParked),
            quotaDenials,
            executions.Count(item => item.Outcome.WasOverridden),
            suggestions,
            evaluated,
            suggestions - evaluated,
            matched,
            executions.Count(item => item.Outcome.MatchedSuggestion == false),
            Latency(executions),
            Tokens(executions),
            Calibration(executions),
            executions
                .GroupBy(ProviderKeyFor)
                .OrderBy(group => group.Key.ProviderName, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Model, StringComparer.Ordinal)
                .ThenBy(group => group.Key.ProviderAlias, StringComparer.Ordinal)
                .Select(group => ProviderMetrics(group.Key, group.ToList()))
                .ToList());
    }

    private static AgentProviderModelMetricsDto ProviderMetrics(
        ProviderKey key,
        IReadOnlyList<EvaluatedExecution> executions) =>
        new(
            key.ProviderAlias,
            key.ProviderName,
            key.Model,
            executions.Count,
            executions.Count(item => item.Record.Status == AgentExecutionStatus.Succeeded),
            executions.Count(item => item.Record.Status == AgentExecutionStatus.Failed),
            executions.Count(item => item.Record.WasCommitted),
            executions.Count(item => item.Record.WasParked),
            executions.Count(item =>
                SanitizeFailureCode(item.Record.FailureCode) ==
                AgentFailureCodes.HostedQuotaDenied),
            executions.Count(item => item.Outcome.WasOverridden),
            executions.Count(HasSuggestion),
            executions.Count(IsEvaluated),
            executions.Count(item => item.Outcome.MatchedSuggestion == true),
            Latency(executions),
            Tokens(executions));

    private static AgentLatencyMetricsDto Latency(
        IReadOnlyList<EvaluatedExecution> executions)
    {
        var values = executions
            .Where(item => item.Record.DurationMs is >= 0)
            .Select(item => item.Record.DurationMs!.Value)
            .Order()
            .ToArray();
        if (values.Length == 0)
            return new AgentLatencyMetricsDto(0, null, null, null);

        return new AgentLatencyMetricsDto(
            values.Length,
            values.Average(value => (double)value),
            Percentile(values, 0.50),
            Percentile(values, 0.95));
    }

    private static AgentTokenMetricsDto Tokens(
        IReadOnlyList<EvaluatedExecution> executions) =>
        new(
            executions.Sum(item => Math.Max(0, item.Record.InputTokens ?? 0)),
            executions.Sum(item => Math.Max(0, item.Record.OutputTokens ?? 0)));

    private static AgentConfidenceCalibrationDto Calibration(
        IReadOnlyList<EvaluatedExecution> executions)
    {
        var samples = executions
            .Where(item =>
                item.Record.Confidence.HasValue &&
                item.Outcome.MatchedSuggestion.HasValue)
            .Select(item => new CalibrationSample(
                item.Record.Confidence!.Value,
                item.Outcome.MatchedSuggestion!.Value))
            .ToList();

        var bins = new[]
        {
            (Label: "[0.0,0.2)", Lower: 0.0, Upper: 0.2, Inclusive: false),
            (Label: "[0.2,0.4)", Lower: 0.2, Upper: 0.4, Inclusive: false),
            (Label: "[0.4,0.6)", Lower: 0.4, Upper: 0.6, Inclusive: false),
            (Label: "[0.6,0.8)", Lower: 0.6, Upper: 0.8, Inclusive: false),
            (Label: "[0.8,1.0]", Lower: 0.8, Upper: 1.0, Inclusive: true)
        };
        var binDtos = bins.Select(bin =>
        {
            var binSamples = samples
                .Where(sample =>
                    sample.Confidence >= bin.Lower &&
                    (sample.Confidence < bin.Upper ||
                     (bin.Inclusive && sample.Confidence <= bin.Upper)))
                .ToList();
            return new AgentConfidenceBinDto(
                bin.Label,
                bin.Lower,
                bin.Upper,
                bin.Inclusive,
                binSamples.Count,
                binSamples.Count(sample => sample.Matched),
                binSamples.Count == 0
                    ? null
                    : binSamples.Average(sample => sample.Confidence),
                binSamples.Count == 0
                    ? null
                    : binSamples.Average(sample => sample.Matched ? 1d : 0d),
                binSamples.Count == 0
                    ? null
                    : binSamples.Average(Brier));
        }).ToList();

        return new AgentConfidenceCalibrationDto(
            samples.Count,
            samples.Count == 0 ? null : samples.Average(Brier),
            binDtos);
    }

    private static double Brier(CalibrationSample sample)
    {
        var observed = sample.Matched ? 1d : 0d;
        return Math.Pow(sample.Confidence - observed, 2);
    }

    private static long Percentile(IReadOnlyList<long> sortedValues, double percentile)
    {
        var index = Math.Max(
            0,
            (int)Math.Ceiling(percentile * sortedValues.Count) - 1);
        return sortedValues[index];
    }

    private static bool HasSuggestion(EvaluatedExecution item) =>
        !string.IsNullOrWhiteSpace(item.Record.SuggestedEvent);

    private static bool IsEvaluated(EvaluatedExecution item) =>
        item.Outcome.Status == Evaluated;

    private static ProviderKey ProviderKeyFor(EvaluatedExecution item) =>
        new(
            SafeLabel(item.Record.ProviderAlias),
            SafeLabel(item.Record.ProviderName),
            SafeLabel(item.Record.Model));

    private static string? SanitizeFailureCode(string? failureCode)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
            return null;

        var code = failureCode.Trim();
        if (code.StartsWith(FlowOsHostedLlmCodes.Quota, StringComparison.Ordinal) ||
            string.Equals(code, AgentFailureCodes.HostedQuotaDenied, StringComparison.Ordinal))
        {
            return AgentFailureCodes.HostedQuotaDenied;
        }
        if (code.StartsWith(TenantEntitlementPolicy.PlanRequiredCode, StringComparison.Ordinal) ||
            string.Equals(code, AgentFailureCodes.EntitlementDenied, StringComparison.Ordinal))
        {
            return AgentFailureCodes.EntitlementDenied;
        }
        if (code.StartsWith(FlowOsHostedLlmCodes.Unavailable, StringComparison.Ordinal) ||
            string.Equals(code, AgentFailureCodes.ProviderConfiguration, StringComparison.Ordinal))
        {
            return AgentFailureCodes.ProviderConfiguration;
        }

        return code switch
        {
            AgentFailureCodes.InvalidModelOutput => code,
            AgentFailureCodes.ProviderAuth => code,
            AgentFailureCodes.ProviderRateLimit => code,
            AgentFailureCodes.ProviderUnavailable => code,
            AgentFailureCodes.ProviderTimeout => code,
            _ => "EXECUTION_FAILED"
        };
    }

    private static bool TryMetadata(
        DomainEvent evt,
        string key,
        out string value)
    {
        value = string.Empty;
        if (evt.Metadata == null)
            return false;

        var pair = evt.Metadata.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(pair.Value))
            return false;

        value = pair.Value;
        return true;
    }

    private static string? SafeLabel(string? value, int maximumLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var sanitized = new string(trimmed
            .Where(character => !char.IsControl(character))
            .ToArray());
        if (sanitized.Length == 0)
            return null;
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }

    private static void ValidateTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
    }

    private static (DateTime FromUtc, DateTime ToUtc) ValidateWindow(
        DateTime fromUtc,
        DateTime toUtc)
    {
        if (fromUtc.Kind != DateTimeKind.Utc || toUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("fromUtc and toUtc must be UTC.");
        if (fromUtc >= toUtc)
            throw new ArgumentException("fromUtc must be earlier than toUtc.");
        if (toUtc - fromUtc > AgentObservabilityLimits.MaximumWindow)
        {
            throw new ArgumentException(
                $"The UTC window cannot exceed {AgentObservabilityLimits.MaximumWindow.TotalDays:0} days.");
        }

        return (fromUtc, toUtc);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private sealed record EvaluatedExecution(
        AgentExecutionRecord Record,
        OutcomeEvaluation Outcome);

    private sealed record OutcomeEvaluation(
        string Status,
        string? Source,
        string? ObservedOutcome,
        bool? MatchedSuggestion,
        DateTime? EvaluatedAtUtc,
        bool WasOverridden,
        string? OverrideActor,
        string? OverrideEvent,
        DateTime? OverriddenAtUtc);

    private sealed record ProviderKey(
        string? ProviderAlias,
        string? ProviderName,
        string? Model);

    private sealed record CalibrationSample(
        double Confidence,
        bool Matched);
}
