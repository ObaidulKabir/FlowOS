using FlowOS.Agents.Abstractions;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Common.Interfaces;
using FlowOS.Core.Common.Services;
using FlowOS.Domain.Enums;
using FlowOS.Workflows.Enums;
using MediatR;
using System.Security.Cryptography;
using System.Text;

namespace FlowOS.Application.Services;

public sealed class AgentTaskRunner : IAgentTaskRunner
{
    private static readonly TimeSpan ExecutionLeaseDuration = TimeSpan.FromMinutes(2);

    private readonly IDecisionPacketBuilder _packetBuilder;
    private readonly IMediator _mediator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDistributedLeaseService _leaseService;
    private readonly IAgentToolHost? _toolHost;
    private readonly IWorkflowAgentFactory _agentFactory;
    private readonly IAgentExecutionRecorder? _executionRecorder;
    private readonly IAgentExecutionHistoryStore? _executionHistory;

    public AgentTaskRunner(
        IDecisionPacketBuilder packetBuilder,
        IMediator mediator,
        IUnitOfWork unitOfWork,
        IDistributedLeaseService leaseService,
        IAgentToolHost? toolHost = null,
        IWorkflowAgentFactory? agentFactory = null,
        IAgentExecutionRecorder? executionRecorder = null,
        IAgentExecutionHistoryStore? executionHistory = null)
    {
        _packetBuilder = packetBuilder;
        _mediator = mediator;
        _unitOfWork = unitOfWork;
        _leaseService = leaseService;
        _toolHost = toolHost;
        _agentFactory = agentFactory ?? new WorkflowAgentFactory(NullPluginBindingRegistryService.Instance);
        _executionRecorder = executionRecorder;
        _executionHistory = executionHistory;
    }

    public Task<AgentTaskRunResult> SuggestAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? agentId = null,
        string? objective = null,
        CancellationToken cancellationToken = default,
        AgentTaskExecutionContext? executionContext = null) =>
        ExecuteAsync(
            tenantId,
            workflowInstanceId,
            agentId,
            objective,
            allowAutoCommit: false,
            requireAgentActor: false,
            cancellationToken,
            executionContext);

    public Task<AgentTaskRunResult> TryRunForCurrentStepAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? agentId = null,
        CancellationToken cancellationToken = default,
        AgentTaskExecutionContext? executionContext = null) =>
        ExecuteAsync(
            tenantId,
            workflowInstanceId,
            agentId,
            "Decide the next legal workflow event",
            allowAutoCommit: true,
            requireAgentActor: true,
            cancellationToken,
            executionContext);

    private async Task<AgentTaskRunResult> ExecuteAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? agentId,
        string? objective,
        bool allowAutoCommit,
        bool requireAgentActor,
        CancellationToken cancellationToken,
        AgentTaskExecutionContext? executionContext)
    {
        var fallbackAgentId = ResolveFallbackActorId(agentId, null);
        var instance = await _unitOfWork.WorkflowInstances
            .GetByIdAsNoTrackingAsync(workflowInstanceId, tenantId, cancellationToken);
        if (instance == null)
        {
            return new AgentTaskRunResult(false, false, "Workflow instance was not found.", null, fallbackAgentId, null, null);
        }

        if (instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed)
        {
            return new AgentTaskRunResult(false, false, "Instance is not running.", null, fallbackAgentId, null, null);
        }

        var packet = await _packetBuilder.BuildAsync(tenantId, workflowInstanceId, objective, cancellationToken);
        if (packet == null)
        {
            return new AgentTaskRunResult(false, false, "Decision packet could not be built.", null, fallbackAgentId, null, null);
        }
        fallbackAgentId = ResolveFallbackActorId(agentId, packet);

        if (!string.IsNullOrWhiteSpace(executionContext?.ExpectedStepId) &&
            !string.Equals(
                executionContext.ExpectedStepId,
                packet.CurrentStepId,
                StringComparison.OrdinalIgnoreCase))
        {
            return new AgentTaskRunResult(
                false,
                false,
                "Workflow is no longer waiting at the queued step.",
                null,
                fallbackAgentId,
                null,
                packet,
                JobId: executionContext.JobId);
        }

        var definition = await _unitOfWork.WorkflowDefinitions
            .GetByIdAsNoTrackingAsync(instance.WorkflowDefinitionId, cancellationToken);
        var step = definition?.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);

        if (requireAgentActor)
        {
            if (!DecisionPacketFactory.IsWaitingStep(step))
            {
                return new AgentTaskRunResult(
                    false,
                    false,
                    "Current step is not a waiting Agent/Either task.",
                    null,
                    fallbackAgentId,
                    null,
                    packet,
                    JobId: executionContext?.JobId);
            }

            if (!StepActor.IsAgentHandled(packet.Actor))
            {
                return new AgentTaskRunResult(
                    false,
                    false,
                    $"Step actor is '{packet.Actor}'.",
                    null,
                    fallbackAgentId,
                    null,
                    packet,
                    JobId: executionContext?.JobId);
            }
        }

        var executionId = ResolveExecutionId(executionContext);
        if (_executionHistory != null)
        {
            var existing = await _executionHistory.GetAsync(
                tenantId,
                executionId,
                cancellationToken);
            if (existing is { Status: not AgentExecutionStatus.Running })
                return FromExecutionRecord(existing, packet);
        }

        var leaseKey = CreateLeaseKey(tenantId, workflowInstanceId, packet.CurrentStepId);
        var leaseOwner = ResolveLeaseOwner(executionContext, executionId);
        var lease = await _leaseService.TryAcquireAsync(
            leaseKey,
            leaseOwner,
            ExecutionLeaseDuration,
            cancellationToken);
        if (lease == null)
        {
            return new AgentTaskRunResult(
                false,
                false,
                "Agent execution is already owned by another worker.",
                null,
                fallbackAgentId,
                null,
                packet,
                ExecutionId: executionId,
                JobId: executionContext?.JobId);
        }

        ResolvedWorkflowAgent? resolved = null;
        var usageFinalized = false;
        var executionStarted = false;
        try
        {
            if (_toolHost != null)
                packet = await _toolHost.PrefetchAsync(packet, cancellationToken);

            try
            {
                resolved = await _agentFactory.ResolveAsync(packet, agentId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidOperationException ex)
            {
                var failure = SanitizedResolutionFailure(ex.Message);
                await RecordResolutionFailureAsync(
                    executionId,
                    executionContext,
                    tenantId,
                    workflowInstanceId,
                    packet,
                    definition,
                    instance.CorrelationId,
                    fallbackAgentId,
                    allowAutoCommit,
                    failure);
                return new AgentTaskRunResult(
                    false,
                    false,
                    failure,
                    null,
                    fallbackAgentId,
                    null,
                    packet,
                    ExecutionId: executionId,
                    JobId: executionContext?.JobId);
            }
            catch
            {
                const string failure = "Agent provider could not be resolved.";
                await RecordResolutionFailureAsync(
                    executionId,
                    executionContext,
                    tenantId,
                    workflowInstanceId,
                    packet,
                    definition,
                    instance.CorrelationId,
                    fallbackAgentId,
                    allowAutoCommit,
                    failure);
                return new AgentTaskRunResult(
                    false,
                    false,
                    failure,
                    null,
                    fallbackAgentId,
                    null,
                    packet,
                    ExecutionId: executionId,
                    JobId: executionContext?.JobId);
            }

            if (_executionRecorder != null)
            {
                executionId = await _executionRecorder.StartAsync(
                    new AgentExecutionStartRequest(
                        tenantId,
                        workflowInstanceId,
                        packet.CurrentStepId,
                        $"Agent:{resolved.ActorId}",
                        allowAutoCommit ? AgentExecutionMode.Live : AgentExecutionMode.Suggest,
                        ExecutionId: executionId,
                        JobId: executionContext?.JobId,
                        Claimant: executionContext?.Claimant,
                        ProviderAlias: resolved.ProviderAlias,
                        ProviderName: resolved.ProviderName,
                        Model: resolved.Model,
                        PromptAlias: packet.Prompt.Alias,
                        RuntimeIdentifier: resolved.RuntimeIdentifier,
                        RuntimeVersion: resolved.Agent.GetType().Assembly.GetName().Version?.ToString(),
                        WorkflowDefinitionId: definition?.Id,
                        WorkflowDefinitionVersion: definition?.Version,
                        ContextBindingRevisionId: definition?.ContextBindingRevisionId,
                        IdempotencyKey: CreateExecutionIdempotencyKey(
                            executionContext,
                            executionId),
                        CorrelationId: instance.CorrelationId ?? workflowInstanceId),
                    cancellationToken);
                executionStarted = true;
            }

            var events = await _unitOfWork.Events.ListByCorrelationIdAsync(workflowInstanceId, cancellationToken);
            var context = AgentContext.FromPacket(packet, events);
            AgentResult result;
            try
            {
                result = await resolved.Agent.ExecuteAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                result = AgentResult.Failure(
                    AgentFailureCodes.ProviderUnavailable,
                    "Agent provider execution failed.");
            }

            if (resolved.UsageReservation != null)
            {
                usageFinalized = true;
                try
                {
                    await resolved.UsageReservation.FinalizeAsync(
                        result.Success,
                        result.Telemetry,
                        CancellationToken.None);
                }
                catch
                {
                    result = AgentResult.Failure(
                        AgentFailureCodes.ProviderUnavailable,
                        "Hosted provider usage accounting failed.",
                        result.Telemetry);
                }
            }

            if (!result.Success)
            {
                await CompleteExecutionAsync(
                    executionId,
                    new AgentExecutionCompletion(
                        false,
                        result.FailureCode,
                        result.FailureReason,
                        result.Telemetry?.HttpStatusCode,
                        result.Telemetry?.InputTokens,
                        result.Telemetry?.OutputTokens),
                    cancellationToken);
                return RunResult(
                    resolved,
                    false,
                    result.FailureReason ?? "Agent provider failed.",
                    result,
                    packet,
                    executionId,
                    executionContext?.JobId);
            }

            var decision = AgentDecisionPolicy.Evaluate(packet, result, resolved.ActorId);
            result = decision.Result;
            var suggestion = decision.Candidate;

            if (allowAutoCommit)
            {
                await PersistInsightAsync(
                    tenantId,
                    workflowInstanceId,
                    resolved.ActorId,
                    result,
                    packet,
                    cancellationToken);
            }

            if (!allowAutoCommit || suggestion == null)
            {
                var park = suggestion == null ? "No legal suggestion." : null;
                await CompleteExecutionAsync(
                    executionId,
                    SuccessfulCompletion(
                        result,
                        suggestion,
                        wasCommitted: false,
                        wasParked: park != null,
                        parkReason: park),
                    cancellationToken);
                return RunResult(
                    resolved,
                    false,
                    park,
                    result,
                    packet,
                    executionId,
                    executionContext?.JobId);
            }

            if (!decision.Evaluation.ShouldCommit)
            {
                await CompleteExecutionAsync(
                    executionId,
                    SuccessfulCompletion(
                        result,
                        suggestion,
                        wasCommitted: false,
                        wasParked: true,
                        parkReason: decision.Evaluation.Reason),
                    cancellationToken);
                return RunResult(
                    resolved,
                    false,
                    decision.Evaluation.Reason,
                    result,
                    packet,
                    executionId,
                    executionContext?.JobId);
            }

            try
            {
                var published = await _mediator.Send(
                    new PublishEventCommand(
                        tenantId,
                        workflowInstanceId,
                        suggestion.EventType,
                        workflowInstanceId,
                        suggestion.Payload.Count == 0 ? packet.EventPayloads : suggestion.Payload,
                        CreateCommitIdempotencyKey(
                            executionContext,
                            executionId,
                            suggestion.EventType),
                        $"Agent:{resolved.ActorId}"),
                    cancellationToken);

                var parkReason = published
                    ? null
                    : "PublishEventCommand declined the suggested event.";
                await CompleteExecutionAsync(
                    executionId,
                    SuccessfulCompletion(
                        result,
                        suggestion,
                        wasCommitted: published,
                        wasParked: !published,
                        parkReason: parkReason),
                    cancellationToken);
                return RunResult(
                    resolved,
                    published,
                    parkReason,
                    result,
                    packet,
                    executionId,
                    executionContext?.JobId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                const string parkReason = "State machine or engine denied the suggestion.";
                await CompleteExecutionAsync(
                    executionId,
                    SuccessfulCompletion(
                        result,
                        suggestion,
                        wasCommitted: false,
                        wasParked: true,
                        parkReason: parkReason),
                    cancellationToken);
                return RunResult(
                    resolved,
                    false,
                    parkReason,
                    result,
                    packet,
                    executionId,
                    executionContext?.JobId);
            }
        }
        catch (OperationCanceledException)
        {
            await TryFinalizeAbandonedUsageAsync(resolved, usageFinalized);

            if (executionStarted && _executionRecorder != null)
            {
                await _executionRecorder.CancelAsync(
                    executionId,
                    "Agent execution was cancelled.",
                    CancellationToken.None);
            }

            throw;
        }
        catch
        {
            await TryFinalizeAbandonedUsageAsync(resolved, usageFinalized);
            if (executionStarted)
            {
                await CompleteExecutionAsync(
                    executionId,
                    new AgentExecutionCompletion(
                        false,
                        AgentFailureCodes.ProviderUnavailable,
                        "Agent execution failed."),
                    CancellationToken.None);
            }
            throw;
        }
        finally
        {
            try
            {
                await _leaseService.ReleaseAsync(
                    leaseKey,
                    leaseOwner,
                    CancellationToken.None);
            }
            catch
            {
                // Lease expiry is the final fallback; preserve the primary result.
            }
        }
    }

    private static async Task TryFinalizeAbandonedUsageAsync(
        ResolvedWorkflowAgent? resolved,
        bool usageFinalized)
    {
        if (resolved?.UsageReservation == null || usageFinalized)
            return;

        try
        {
            await resolved.UsageReservation.FinalizeAsync(
                false,
                null,
                CancellationToken.None);
        }
        catch
        {
            // Preserve the primary failure without exposing store details.
        }
    }

    private async Task RecordResolutionFailureAsync(
        Guid executionId,
        AgentTaskExecutionContext? executionContext,
        Guid tenantId,
        Guid workflowInstanceId,
        DecisionPacket packet,
        FlowOS.Workflows.Domain.WorkflowDefinition? definition,
        Guid? correlationId,
        string actorId,
        bool allowAutoCommit,
        string failure)
    {
        if (_executionRecorder == null)
            return;

        try
        {
            await _executionRecorder.StartAsync(
                new AgentExecutionStartRequest(
                    tenantId,
                    workflowInstanceId,
                    packet.CurrentStepId,
                    $"Agent:{actorId}",
                    allowAutoCommit
                        ? AgentExecutionMode.Live
                        : AgentExecutionMode.Suggest,
                    ExecutionId: executionId,
                    JobId: executionContext?.JobId,
                    Claimant: executionContext?.Claimant,
                    ProviderAlias: packet.Provider?.Alias,
                    ProviderName: packet.Provider?.ProviderName,
                    Model: packet.Provider?.Model,
                    PromptAlias: packet.Prompt.Alias,
                    WorkflowDefinitionId: definition?.Id,
                    WorkflowDefinitionVersion: definition?.Version,
                    ContextBindingRevisionId: definition?.ContextBindingRevisionId,
                    IdempotencyKey: CreateExecutionIdempotencyKey(
                        executionContext,
                        executionId),
                    CorrelationId: correlationId ?? workflowInstanceId),
                CancellationToken.None);
            await _executionRecorder.CompleteAsync(
                executionId,
                new AgentExecutionCompletion(
                    false,
                    ResolutionFailureCode(failure),
                    failure),
                CancellationToken.None);
        }
        catch
        {
            // Provider resolution remains safely reported even if audit storage fails.
        }
    }

    private Task CompleteExecutionAsync(
        Guid? executionId,
        AgentExecutionCompletion completion,
        CancellationToken cancellationToken)
    {
        if (!executionId.HasValue || _executionRecorder == null)
            return Task.CompletedTask;

        return _executionRecorder.CompleteAsync(
            executionId.Value,
            completion,
            CancellationToken.None);
    }

    private static AgentExecutionCompletion SuccessfulCompletion(
        AgentResult result,
        SuggestedAction? suggestion,
        bool wasCommitted,
        bool wasParked,
        string? parkReason) =>
        new(
            true,
            HttpStatusCode: result.Telemetry?.HttpStatusCode,
            InputTokens: result.Telemetry?.InputTokens,
            OutputTokens: result.Telemetry?.OutputTokens,
            SuggestedEvent: suggestion?.EventType,
            Confidence: suggestion?.Confidence,
            WasCommitted: wasCommitted,
            WasParked: wasParked,
            ParkReason: parkReason);

    private static AgentTaskRunResult RunResult(
        ResolvedWorkflowAgent resolved,
        bool autoCommitted,
        string? parkReason,
        AgentResult result,
        DecisionPacket packet,
        Guid executionId,
        Guid? jobId) =>
        new(
            true,
            autoCommitted,
            null,
            parkReason,
            resolved.ActorId,
            result,
            packet,
            resolved.RuntimeIdentifier,
            resolved.ProviderAlias,
            resolved.ProviderName,
            resolved.Model,
            executionId,
            jobId);

    private static AgentTaskRunResult FromExecutionRecord(
        FlowOS.Domain.Entities.AgentExecutionRecord record,
        DecisionPacket packet)
    {
        AgentResult result;
        if (record.Success == false)
        {
            result = AgentResult.Failure(
                record.FailureCode ?? AgentFailureCodes.ProviderUnavailable,
                record.SanitizedFailure ?? "Agent execution failed.",
                new AgentTelemetry(
                    record.HttpStatusCode,
                    InputTokens: record.InputTokens,
                    OutputTokens: record.OutputTokens));
        }
        else if (!string.IsNullOrWhiteSpace(record.SuggestedEvent))
        {
            result = AgentResult.WithActions(
                "Reconstructed from the durable agent execution record.",
                [
                    new SuggestedAction(
                        record.SuggestedEvent,
                        "Durable execution result.",
                        record.Confidence ?? 0)
                ]);
            result.Telemetry = new AgentTelemetry(
                record.HttpStatusCode,
                InputTokens: record.InputTokens,
                OutputTokens: record.OutputTokens);
        }
        else
        {
            result = AgentResult.FromInsight(
                "Reconstructed from the durable agent execution record.");
            result.Telemetry = new AgentTelemetry(
                record.HttpStatusCode,
                InputTokens: record.InputTokens,
                OutputTokens: record.OutputTokens);
        }

        var actorId = record.Actor.StartsWith("Agent:", StringComparison.OrdinalIgnoreCase)
            ? record.Actor["Agent:".Length..]
            : record.Actor;
        return new AgentTaskRunResult(
            true,
            record.WasCommitted,
            null,
            record.ParkReason,
            actorId,
            result,
            packet,
            record.RuntimeIdentifier,
            record.ProviderAlias,
            record.ProviderName,
            record.Model,
            record.ExecutionId,
            record.JobId);
    }

    private static Guid ResolveExecutionId(
        AgentTaskExecutionContext? executionContext)
    {
        if (executionContext?.ExecutionId is { } explicitId &&
            explicitId != Guid.Empty)
        {
            return explicitId;
        }

        if (executionContext?.JobId is not { } jobId || jobId == Guid.Empty)
            return Guid.NewGuid();

        var material = Encoding.UTF8.GetBytes(
            $"agent-job:{jobId:N}:attempt:{Math.Max(1, executionContext.Attempt ?? 1)}");
        var digest = SHA256.HashData(material);
        return new Guid(digest.AsSpan(0, 16));
    }

    private static string CreateLeaseKey(
        Guid tenantId,
        Guid workflowInstanceId,
        string stepId) =>
        $"agent-call:{tenantId:N}:{workflowInstanceId:N}:{stepId.Trim().ToLowerInvariant()}";

    private static string ResolveLeaseOwner(
        AgentTaskExecutionContext? executionContext,
        Guid executionId)
    {
        var claimant = executionContext?.Claimant;
        return string.IsNullOrWhiteSpace(claimant)
            ? $"execution:{executionId:N}"
            : $"{claimant.Trim()}:{executionId:N}";
    }

    private static string CreateExecutionIdempotencyKey(
        AgentTaskExecutionContext? executionContext,
        Guid executionId) =>
        executionContext?.JobId is { } jobId && jobId != Guid.Empty
            ? $"agent-job:{jobId:N}:attempt:{Math.Max(1, executionContext.Attempt ?? 1)}"
            : $"agent-execution:{executionId:N}";

    private static string CreateCommitIdempotencyKey(
        AgentTaskExecutionContext? executionContext,
        Guid executionId,
        string eventType) =>
        executionContext?.JobId is { } jobId && jobId != Guid.Empty
            ? $"agent-commit:job:{jobId:N}:{eventType}"
            : $"agent-commit:execution:{executionId:N}:{eventType}";

    private static string ResolveFallbackActorId(
        string? requestedAgentId,
        DecisionPacket? packet)
    {
        var provider = packet?.Provider;
        if (AgentProviderKinds.IsFlowOsHosted(provider?.ProviderName) ||
            AgentProviderKinds.IsFlowOsHosted(provider?.Alias))
        {
            return AgentProviderKinds.FlowosHosted;
        }

        if (AgentProviderKinds.IsByoLlm(provider?.ProviderName) &&
            !string.IsNullOrWhiteSpace(provider?.Alias))
        {
            return provider.Alias.Trim();
        }

        if (string.Equals(
                provider?.ProviderName,
                AgentProviderKinds.FlowosRisk,
                StringComparison.OrdinalIgnoreCase))
        {
            return AgentProviderKinds.FlowosRisk;
        }

        if (!string.IsNullOrWhiteSpace(requestedAgentId) &&
            !string.Equals(requestedAgentId.Trim(), "RiskAnalysisAgent", StringComparison.OrdinalIgnoreCase))
        {
            return requestedAgentId.Trim();
        }

        return AgentProviderKinds.FlowosRisk;
    }

    private static string SanitizedResolutionFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Agent provider could not be resolved.";

        var knownSafePrefixes = new[]
        {
            FlowOsHostedLlmCodes.Unavailable,
            FlowOsHostedLlmCodes.Quota,
            TenantEntitlementPolicy.PlanRequiredCode,
            "No hosted agent is registered",
            "Agent provider configuration is unavailable"
        };
        return knownSafePrefixes.Any(prefix =>
            message.StartsWith(prefix, StringComparison.Ordinal))
            ? message[..Math.Min(message.Length, 1000)]
            : "Agent provider could not be resolved.";
    }

    private static string ResolutionFailureCode(string failure)
    {
        if (failure.StartsWith(FlowOsHostedLlmCodes.Quota, StringComparison.Ordinal))
            return AgentFailureCodes.HostedQuotaDenied;
        if (failure.StartsWith(TenantEntitlementPolicy.PlanRequiredCode, StringComparison.Ordinal))
            return AgentFailureCodes.EntitlementDenied;

        return AgentFailureCodes.ProviderConfiguration;
    }

    private async Task PersistInsightAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string agentId,
        AgentResult result,
        DecisionPacket packet,
        CancellationToken cancellationToken)
    {
        if (!result.Success)
            return;

        var insight = result.Insight;
        if (string.IsNullOrWhiteSpace(insight) && result.SuggestedActions.Count > 0)
        {
            var top = result.SuggestedActions[0];
            insight = $"Suggested {top.EventType} ({top.Confidence:0.00}): {top.Reason}";
        }

        if (string.IsNullOrWhiteSpace(insight))
            insight = "Agent produced no insight.";

        await _mediator.Send(
            new PublishAgentInsightCommand(
                tenantId,
                workflowInstanceId,
                agentId,
                insight,
                packet.Objective,
                workflowInstanceId),
            cancellationToken);
    }
}
