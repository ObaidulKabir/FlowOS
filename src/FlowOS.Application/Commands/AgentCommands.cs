using System;
using MediatR;
using FlowOS.Application.Common.Interfaces;

namespace FlowOS.Application.Commands;

public record PublishAgentInsightCommand(
    Guid TenantId,
    Guid WorkflowInstanceId,
    string AgentId,
    string Insight,
    string ContextObjective,
    Guid? CorrelationId = null,
    string? StepId = null,
    string? SuggestedEvent = null,
    double? Confidence = null,
    string? ProviderName = null
) : IRequest<bool>, IPolicySecuredCommand;
