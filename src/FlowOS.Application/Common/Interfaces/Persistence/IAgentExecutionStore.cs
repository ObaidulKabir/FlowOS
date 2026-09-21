using FlowOS.Domain.Entities;
using FlowOS.Domain.Enums;

namespace FlowOS.Application.Common.Interfaces.Persistence;

public sealed record AgentExecutionStartRequest(
    Guid TenantId,
    Guid WorkflowInstanceId,
    string StepId,
    string Actor,
    AgentExecutionMode Mode,
    Guid? ExecutionId = null,
    Guid? JobId = null,
    string? Claimant = null,
    string? ProviderAlias = null,
    string? ProviderName = null,
    string? Model = null,
    string? PromptAlias = null,
    string? RuntimeIdentifier = null,
    string? RuntimeVersion = null,
    Guid? WorkflowDefinitionId = null,
    int? WorkflowDefinitionVersion = null,
    Guid? ContextBindingId = null,
    Guid? ContextBindingRevisionId = null,
    long? ContextVersion = null,
    string? IdempotencyKey = null,
    Guid? CorrelationId = null,
    DateTime? StartedAtUtc = null);

public sealed record AgentExecutionCompletion(
    bool Success,
    string? FailureCode = null,
    string? SanitizedFailure = null,
    int? HttpStatusCode = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    string? SuggestedEvent = null,
    double? Confidence = null,
    bool WasCommitted = false,
    bool WasParked = false,
    string? ParkReason = null,
    DateTime? EndedAtUtc = null);

public sealed record AgentExecutionHistoryQuery(
    Guid TenantId,
    Guid? WorkflowInstanceId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    AgentExecutionStatus? Status = null,
    int Limit = 100);

public interface IAgentExecutionRecorder
{
    Task<Guid> StartAsync(
        AgentExecutionStartRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteAsync(
        Guid executionId,
        AgentExecutionCompletion completion,
        CancellationToken cancellationToken = default);

    Task<bool> CancelAsync(
        Guid executionId,
        string? sanitizedReason = null,
        CancellationToken cancellationToken = default);

    Task<bool> RecordObservedOutcomeAsync(
        Guid executionId,
        string observedOutcome,
        bool? matchedSuggestion,
        CancellationToken cancellationToken = default);

    Task<bool> RecordOverrideAsync(
        Guid executionId,
        string overrideActor,
        string? overrideEvent,
        string overrideReason,
        CancellationToken cancellationToken = default);
}

public interface IAgentExecutionHistoryStore
{
    Task<AgentExecutionRecord?> GetAsync(
        Guid tenantId,
        Guid executionId,
        CancellationToken cancellationToken = default);

    Task<AgentExecutionRecord?> GetLatestForJobAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentExecutionRecord>> ListAsync(
        AgentExecutionHistoryQuery query,
        CancellationToken cancellationToken = default);
}
