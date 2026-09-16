using FlowOS.Agents.Abstractions;
using FlowOS.Application.Commands;
using FlowOS.Application.Common.Interfaces;
using FlowOS.Application.Common.Interfaces.Persistence;
using FlowOS.Core.Common.Services;
using FlowOS.Domain.Enums;
using FlowOS.Workflows.Enums;
using MediatR;

namespace FlowOS.Application.Services;

public sealed class AgentTaskRunner : IAgentTaskRunner
{
    private static readonly AsyncLocal<HashSet<string>?> InFlight = new();

    private readonly IDecisionPacketBuilder _packetBuilder;
    private readonly IMediator _mediator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAgentToolHost? _toolHost;
    private readonly IWorkflowAgentFactory _agentFactory;

    public AgentTaskRunner(
        IDecisionPacketBuilder packetBuilder,
        IMediator mediator,
        IUnitOfWork unitOfWork,
        IAgentToolHost? toolHost = null,
        IWorkflowAgentFactory? agentFactory = null)
    {
        _packetBuilder = packetBuilder;
        _mediator = mediator;
        _unitOfWork = unitOfWork;
        _toolHost = toolHost;
        _agentFactory = agentFactory ?? new WorkflowAgentFactory(NullPluginBindingRegistryService.Instance);
    }

    public Task<AgentTaskRunResult> SuggestAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string agentId,
        string? objective = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(tenantId, workflowInstanceId, agentId, objective, allowAutoCommit: false, requireAgentActor: false, cancellationToken);

    public Task<AgentTaskRunResult> TryRunForCurrentStepAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string? agentId = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            tenantId,
            workflowInstanceId,
            string.IsNullOrWhiteSpace(agentId) ? "RiskAnalysisAgent" : agentId,
            "Decide the next legal workflow event",
            allowAutoCommit: true,
            requireAgentActor: true,
            cancellationToken);

    private async Task<AgentTaskRunResult> ExecuteAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string agentId,
        string? objective,
        bool allowAutoCommit,
        bool requireAgentActor,
        CancellationToken cancellationToken)
    {
        var instance = await _unitOfWork.WorkflowInstances
            .GetByIdAsNoTrackingAsync(workflowInstanceId, tenantId, cancellationToken);
        if (instance == null)
        {
            return new AgentTaskRunResult(false, false, "Workflow instance was not found.", null, agentId, null, null);
        }

        if (instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed)
        {
            return new AgentTaskRunResult(false, false, "Instance is not running.", null, agentId, null, null);
        }

        var packet = await _packetBuilder.BuildAsync(tenantId, workflowInstanceId, objective, cancellationToken);
        if (packet == null)
        {
            return new AgentTaskRunResult(false, false, "Decision packet could not be built.", null, agentId, null, null);
        }

        if (_toolHost != null)
            packet = await _toolHost.PrefetchAsync(packet, cancellationToken);

        var definition = await _unitOfWork.WorkflowDefinitions
            .GetByIdAsNoTrackingAsync(instance.WorkflowDefinitionId, cancellationToken);
        var step = definition?.Steps.FirstOrDefault(s => s.StepId == instance.CurrentStepId);

        if (requireAgentActor)
        {
            if (!DecisionPacketFactory.IsWaitingStep(step))
            {
                return new AgentTaskRunResult(false, false, "Current step is not a waiting Agent/Either task.", null, agentId, null, packet);
            }

            if (!StepActor.IsAgentHandled(packet.Actor))
            {
                return new AgentTaskRunResult(false, false, $"Step actor is '{packet.Actor}'.", null, agentId, null, packet);
            }
        }

        var gate = $"{workflowInstanceId:N}:{packet.CurrentStepId}";
        var inflight = InFlight.Value ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!inflight.Add(gate))
        {
            return new AgentTaskRunResult(false, false, "Agent already running for this step.", null, agentId, null, packet);
        }

        try
        {
            IWorkflowAgent agent;
            try
            {
                agent = await _agentFactory.CreateAsync(packet, agentId, cancellationToken);
            }
            catch (Exception ex)
            {
                return new AgentTaskRunResult(false, false, ex.Message, null, agentId, null, packet);
            }

            var events = await _unitOfWork.Events.ListByCorrelationIdAsync(workflowInstanceId, cancellationToken);
            var context = AgentContext.FromPacket(packet, events);
            var result = await agent.ExecuteAsync(context);
            result = AgentSuggestionContract.RestrictToLegalEvents(result, packet.LegalNextStepEvents);

            var suggestion = result.SuggestedActions
                .OrderByDescending(action => action.Confidence)
                .FirstOrDefault();

            if (allowAutoCommit)
            {
                await PersistInsightAsync(tenantId, workflowInstanceId, agentId, result, packet, cancellationToken);
            }

            if (!allowAutoCommit || suggestion == null)
            {
                var park = suggestion == null ? "No legal suggestion." : null;
                return new AgentTaskRunResult(true, false, null, park, agentId, result, packet);
            }

            if (!AutoCommitEvaluator.CanCommit(packet, suggestion, out var parkReason))
            {
                return new AgentTaskRunResult(true, false, null, parkReason, agentId, result, packet);
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
                        $"agent-commit:{workflowInstanceId:N}:{packet.CurrentStepId}:{suggestion.EventType}",
                        $"Agent:{agentId}"),
                    cancellationToken);

                return new AgentTaskRunResult(
                    true,
                    published,
                    null,
                    published ? null : "PublishEventCommand declined the suggested event.",
                    agentId,
                    result,
                    packet);
            }
            catch (Exception ex)
            {
                return new AgentTaskRunResult(
                    true,
                    false,
                    null,
                    $"State machine or engine denied the suggestion: {ex.Message}",
                    agentId,
                    result,
                    packet);
            }
        }
        finally
        {
            inflight.Remove(gate);
        }
    }

    private async Task PersistInsightAsync(
        Guid tenantId,
        Guid workflowInstanceId,
        string agentId,
        AgentResult result,
        DecisionPacket packet,
        CancellationToken cancellationToken)
    {
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
