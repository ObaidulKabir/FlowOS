using FlowOS.Domain.Enums;

namespace FlowOS.Domain.Entities;

public sealed class AgentExecutionRecord
{
    public Guid ExecutionId { get; private set; }
    public Guid? JobId { get; private set; }
    public string? Claimant { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public string StepId { get; private set; } = string.Empty;
    public string Actor { get; private set; } = string.Empty;
    public AgentExecutionMode Mode { get; private set; }
    public AgentExecutionStatus Status { get; private set; }
    public AgentExecutionOutcome DecisionOutcome { get; private set; }
    public string? ProviderAlias { get; private set; }
    public string? ProviderName { get; private set; }
    public string? Model { get; private set; }
    public string? PromptAlias { get; private set; }
    public string? RuntimeIdentifier { get; private set; }
    public string? RuntimeVersion { get; private set; }
    public Guid? WorkflowDefinitionId { get; private set; }
    public int? WorkflowDefinitionVersion { get; private set; }
    public Guid? ContextBindingId { get; private set; }
    public Guid? ContextBindingRevisionId { get; private set; }
    public long? ContextVersion { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    public long? DurationMs { get; private set; }
    public bool? Success { get; private set; }
    public string? FailureCode { get; private set; }
    public string? SanitizedFailure { get; private set; }
    public int? HttpStatusCode { get; private set; }
    public long? InputTokens { get; private set; }
    public long? OutputTokens { get; private set; }
    public string? SuggestedEvent { get; private set; }
    public double? Confidence { get; private set; }
    public bool WasCommitted { get; private set; }
    public bool WasParked { get; private set; }
    public string? ParkReason { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public string? ObservedOutcome { get; private set; }
    public bool? OutcomeMatchedSuggestion { get; private set; }
    public DateTime? OutcomeEvaluatedAtUtc { get; private set; }
    public bool WasOverridden { get; private set; }
    public string? OverrideActor { get; private set; }
    public string? OverrideEvent { get; private set; }
    public string? OverrideReason { get; private set; }
    public DateTime? OverriddenAtUtc { get; private set; }

    private AgentExecutionRecord()
    {
    }

    public AgentExecutionRecord(
        Guid executionId,
        Guid? jobId,
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId,
        string actor,
        AgentExecutionMode mode,
        DateTime startedAtUtc,
        string? claimant = null)
    {
        if (executionId == Guid.Empty)
            throw new ArgumentException("ExecutionId is required.", nameof(executionId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (workflowInstanceId == Guid.Empty)
            throw new ArgumentException("WorkflowInstanceId is required.", nameof(workflowInstanceId));

        ExecutionId = executionId;
        JobId = jobId;
        Claimant = Optional(claimant, 200);
        TenantId = tenantId;
        WorkflowInstanceId = workflowInstanceId;
        StepId = Required(stepId, 200, nameof(stepId));
        Actor = Required(actor, 200, nameof(actor));
        Mode = mode;
        Status = AgentExecutionStatus.Running;
        DecisionOutcome = AgentExecutionOutcome.Pending;
        StartedAtUtc = AsUtc(startedAtUtc);
    }

    public void SetProviderMetadata(
        string? providerAlias,
        string? providerName,
        string? model,
        string? promptAlias)
    {
        EnsureRunning();
        ProviderAlias = Optional(providerAlias, 200);
        ProviderName = Optional(providerName, 200);
        Model = Optional(model, 200);
        PromptAlias = Optional(promptAlias, 200);
    }

    public void SetVersionMetadata(
        string? runtimeIdentifier,
        string? runtimeVersion,
        Guid? workflowDefinitionId,
        int? workflowDefinitionVersion,
        Guid? contextBindingId,
        Guid? contextBindingRevisionId,
        long? contextVersion)
    {
        EnsureRunning();
        RuntimeIdentifier = Optional(runtimeIdentifier, 200);
        RuntimeVersion = Optional(runtimeVersion, 100);
        WorkflowDefinitionId = workflowDefinitionId;
        WorkflowDefinitionVersion = workflowDefinitionVersion;
        ContextBindingId = contextBindingId;
        ContextBindingRevisionId = contextBindingRevisionId;
        ContextVersion = contextVersion;
    }

    public void SetTraceIdentifiers(string? idempotencyKey, Guid? correlationId)
    {
        EnsureRunning();
        IdempotencyKey = Optional(idempotencyKey, 300);
        CorrelationId = correlationId;
    }

    public void Complete(
        bool success,
        DateTime endedAtUtc,
        string? failureCode,
        string? sanitizedFailure,
        int? httpStatusCode,
        long? inputTokens,
        long? outputTokens,
        string? suggestedEvent,
        double? confidence,
        bool wasCommitted,
        bool wasParked,
        string? parkReason)
    {
        EnsureRunning();
        ValidateTelemetry(inputTokens, outputTokens, confidence);

        var endedAt = AsUtc(endedAtUtc);
        if (endedAt < StartedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(endedAtUtc), "Execution end cannot precede its start.");
        if (wasCommitted && wasParked)
            throw new ArgumentException("An execution cannot be both committed and parked.");
        if (!success && wasCommitted)
            throw new ArgumentException("A failed execution cannot be committed.");

        EndedAtUtc = endedAt;
        DurationMs = (long)(endedAt - StartedAtUtc).TotalMilliseconds;
        Success = success;
        Status = success ? AgentExecutionStatus.Succeeded : AgentExecutionStatus.Failed;
        FailureCode = Optional(failureCode, 100);
        SanitizedFailure = Optional(sanitizedFailure, 2000);
        HttpStatusCode = httpStatusCode;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        SuggestedEvent = Optional(suggestedEvent, 200);
        Confidence = confidence;
        WasCommitted = wasCommitted;
        WasParked = wasParked;
        ParkReason = Optional(parkReason, 1000);
        DecisionOutcome = ResolveOutcome(success, SuggestedEvent, wasCommitted, wasParked);
    }

    public void Cancel(DateTime endedAtUtc, string? sanitizedReason)
    {
        EnsureRunning();
        var endedAt = AsUtc(endedAtUtc);
        if (endedAt < StartedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(endedAtUtc), "Execution end cannot precede its start.");

        EndedAtUtc = endedAt;
        DurationMs = (long)(endedAt - StartedAtUtc).TotalMilliseconds;
        Success = false;
        Status = AgentExecutionStatus.Cancelled;
        DecisionOutcome = AgentExecutionOutcome.Skipped;
        SanitizedFailure = Optional(sanitizedReason, 2000);
    }

    public void RecordObservedOutcome(
        string observedOutcome,
        bool? matchedSuggestion,
        DateTime evaluatedAtUtc)
    {
        ObservedOutcome = Required(observedOutcome, 500, nameof(observedOutcome));
        OutcomeMatchedSuggestion = matchedSuggestion;
        OutcomeEvaluatedAtUtc = AsUtc(evaluatedAtUtc);
    }

    public void RecordOverride(
        string overrideActor,
        string? overrideEvent,
        string overrideReason,
        DateTime overriddenAtUtc)
    {
        WasOverridden = true;
        OverrideActor = Required(overrideActor, 200, nameof(overrideActor));
        OverrideEvent = Optional(overrideEvent, 200);
        OverrideReason = Required(overrideReason, 1000, nameof(overrideReason));
        OverriddenAtUtc = AsUtc(overriddenAtUtc);
    }

    private void EnsureRunning()
    {
        if (Status != AgentExecutionStatus.Running)
            throw new InvalidOperationException($"Execution '{ExecutionId}' is already terminal.");
    }

    private static AgentExecutionOutcome ResolveOutcome(
        bool success,
        string? suggestedEvent,
        bool wasCommitted,
        bool wasParked)
    {
        if (!success)
            return AgentExecutionOutcome.Failed;
        if (wasCommitted)
            return AgentExecutionOutcome.Committed;
        if (wasParked)
            return AgentExecutionOutcome.Parked;
        return string.IsNullOrWhiteSpace(suggestedEvent)
            ? AgentExecutionOutcome.Skipped
            : AgentExecutionOutcome.Suggested;
    }

    private static void ValidateTelemetry(long? inputTokens, long? outputTokens, double? confidence)
    {
        if (inputTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(inputTokens));
        if (outputTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(outputTokens));
        if (confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between zero and one.");
    }

    private static string Required(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
        return trimmed;
    }

    private static string? Optional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
